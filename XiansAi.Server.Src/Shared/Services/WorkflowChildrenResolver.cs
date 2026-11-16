using Shared.Utils;
using Shared.Utils.Temporal;

namespace Shared.Services;

public interface IWorkflowChildrenResolver
{
    /// <summary>
    /// Returns the IDs of all child workflows (and their children, up to a fixed depth) started
    /// by the given workflows. The given workflow IDs themselves are not included.
    /// </summary>
    Task<List<string>> GetDescendantWorkflowIdsAsync(string tenantId, IReadOnlyCollection<string> parentWorkflowIds);
}

/// <summary>
/// Finds child workflows through Temporal visibility, using the <c>ParentWorkflowId</c> that Temporal
/// records on executions started with StartChildWorkflow/ExecuteChildWorkflow. Workflows started via
/// the Temporal client (e.g. from an activity) have no parent link and are not found.
/// </summary>
public class WorkflowChildrenResolver : IWorkflowChildrenResolver
{
    private const int MaxDepth = 3;
    private const int MaxResults = 1000;
    private const int QueryBatchSize = 50;

    private readonly ITemporalGatewayService _temporalGatewayService;
    private readonly ILogger<WorkflowChildrenResolver> _logger;

    public WorkflowChildrenResolver(
        ITemporalGatewayService temporalGatewayService,
        ILogger<WorkflowChildrenResolver> logger)
    {
        _temporalGatewayService = temporalGatewayService ?? throw new ArgumentNullException(nameof(temporalGatewayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<string>> GetDescendantWorkflowIdsAsync(string tenantId, IReadOnlyCollection<string> parentWorkflowIds)
    {
        var seen = new HashSet<string>(parentWorkflowIds, StringComparer.Ordinal);
        var descendants = new List<string>();
        var currentLevel = parentWorkflowIds.Distinct(StringComparer.Ordinal).ToList();

        try
        {
            for (var depth = 0; depth < MaxDepth && currentLevel.Count > 0 && descendants.Count < MaxResults; depth++)
            {
                var nextLevel = new List<string>();

                foreach (var batch in currentLevel.Chunk(QueryBatchSize))
                {
                    var idList = string.Join(", ", batch.Select(id => $"'{EscapeQueryValue(id)}'"));
                    var query = $"ParentWorkflowId IN ({idList})";

                    await foreach (var client in _temporalGatewayService.GetClientsAsync(tenantId))
                    {
                        await foreach (var workflow in client.ListWorkflowsAsync(query))
                        {
                            // Only return workflows that belong to the caller's tenant.
                            if (string.IsNullOrEmpty(workflow.Id) || !workflow.Id.StartsWith(tenantId + ":", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            if (seen.Add(workflow.Id))
                            {
                                descendants.Add(workflow.Id);
                                nextLevel.Add(workflow.Id);
                            }

                            if (descendants.Count >= MaxResults)
                            {
                                _logger.LogWarning("Child workflow lookup hit the limit of {MaxResults} workflows; results are truncated", MaxResults);
                                return descendants;
                            }
                        }
                    }
                }

                currentLevel = nextLevel;
            }
        }
        catch (Exception ex)
        {
            // e.g. the Temporal visibility store does not support ParentWorkflowId - degrade to "no children".
            _logger.LogWarning(ex, "Failed to look up child workflows for tenant {TenantId}; returning {Count} found so far",
                LogSanitizer.Sanitize(tenantId), descendants.Count);
        }

        return descendants;
    }

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
