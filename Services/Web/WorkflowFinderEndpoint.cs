using System.Text.Json;
using XiansAi.Server.Auth;
using XiansAi.Server.Temporal;

namespace XiansAi.Server.Services.Web;

public class WorkflowFinderEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowFinderEndpoint> _logger;

    private readonly ITenantContext _tenantContext;

    public WorkflowFinderEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowFinderEndpoint> logger,
        ITenantContext tenantContext)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IResult> GetWorkflow(string workflowId)
    {
        var client = _clientService.GetClient();
        var workflowHandle = client.GetWorkflowHandle(workflowId);
        var workflowDescription = await workflowHandle.DescribeAsync();

        _logger.LogDebug("Found workflow {workflow}", JsonSerializer.Serialize(workflowDescription));

        var tenantId = workflowDescription.Memo.TryGetValue("tenantId", out var tenantIdValue) ? tenantIdValue.ToString() : null;
        var userId = workflowDescription.Memo.TryGetValue("userId", out var userIdValue) ? userIdValue.Payload.Data.ToStringUtf8().Replace("\"", "") : null;

        _logger.LogDebug("Found workflow {workflowId} for tenant {tenantId} and user {userId}", workflowId, tenantId, userId);
        var workflow = new
        {
            workflowDescription.Id,
            workflowDescription.RunId,
            workflowDescription.WorkflowType,
            Status = workflowDescription.Status.ToString(),
            workflowDescription.StartTime,
            workflowDescription.ExecutionTime,
            workflowDescription.CloseTime,
            workflowDescription.ParentId,
            workflowDescription.ParentRunId,
            TenantId = tenantId,
            Owner = userId
        };
        return Results.Ok(workflow);
    }

    public async Task<IResult> GetWorkflows(DateTime? startTime, DateTime? endTime, string? owner)
    {
        _logger.LogInformation("Getting list of workflows at: {Time}", DateTime.UtcNow);

        try
        {
            var client = _clientService.GetClient();
            var workflows = new List<object>();

            var listQuery = "";

            if (startTime != null && endTime != null)
            {
                listQuery = $"ExecutionTime between '{startTime.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:sszzz}' and '{endTime.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:sszzz}'";
            }
            else if (startTime != null)
            {
                listQuery = $"ExecutionTime >= '{startTime.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:sszzz}'";
            }
            else if (endTime != null)
            {
                listQuery = $"ExecutionTime <= '{endTime.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:sszzz}'";
            }

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
            {
                _logger.LogDebug("Found workflow {workflow}", JsonSerializer.Serialize(workflow));
                var tenantId = workflow.Memo.TryGetValue("tenantId", out var tenantIdValue) ? tenantIdValue.Payload.Data.ToStringUtf8().Replace("\"", "") : null;
                var userId = workflow.Memo.TryGetValue("userId", out var userIdValue) ? userIdValue.Payload.Data.ToStringUtf8().Replace("\"", "") : null;

                _logger.LogDebug("Found workflow {workflowId} for tenant {tenantId} and user {userId}", workflow.Id, tenantId, userId);

                if ("current".Equals(owner, StringComparison.OrdinalIgnoreCase) && _tenantContext.LoggedInUser != userId)
                {
                    continue;
                }

                workflows.Add(new
                {
                    workflow.Id,
                    workflow.WorkflowType,
                    workflow.RunId,
                    Status = workflow.Status.ToString(),
                    workflow.StartTime,
                    workflow.ExecutionTime,
                    workflow.CloseTime,
                    TenantId = tenantId,
                    Owner = userId
                });
            }

            _logger.LogInformation("Successfully retrieved {Count} workflows", workflows.Count);
            return Results.Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return Results.Problem(
                title: "Failed to retrieve workflows",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
