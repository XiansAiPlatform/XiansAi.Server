using System.Text.Json.Serialization;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Temporal;
using Temporalio.Api.Sdk.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Shared.Services.HealthChecks;

[JsonConverter(typeof(JsonStringEnumConverter<WebhookTargetStatus>))]
public enum WebhookTargetStatus
{
    /// <summary>The activation's workflow answered; <c>methods</c> lists its update handlers.</summary>
    [JsonStringEnumMemberName("running")] Running,

    /// <summary>No workflow with this id exists for the activation, so its methods can't be listed.</summary>
    [JsonStringEnumMemberName("notRunning")] NotRunning,

    /// <summary>The workflow exists but nothing answered (agent worker stopped, or timed out).</summary>
    [JsonStringEnumMemberName("unavailable")] Unavailable
}

public sealed record WebhookTargetMethod(string Name, string? Description);

/// <summary>
/// One of the agent's workflows as a webhook-eval target for an activation. <c>WorkflowType</c> is the
/// flow name without the agent prefix, which is what a webhook eval stores as its workflow.
/// </summary>
public sealed record WebhookTargetWorkflow(
    string WorkflowType,
    string WorkflowId,
    bool Activable,
    WebhookTargetStatus Status,
    string? Detail,
    List<WebhookTargetMethod> Methods);

public interface IWebhookTargetDiscovery
{
    Task<List<WebhookTargetWorkflow>> DiscoverAsync(AgentActivation activation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lists the webhook targets (workflow + Temporal Update method) a webhook eval can call on an
/// activation. Workflow types come from the agent's registered flow definitions. Update handler
/// names aren't stored by the server, so they're read live with Temporal's built-in
/// <c>__temporal_workflow_metadata</c> query against the activation's workflow; that only works while
/// the workflow exists and an agent worker is running.
/// </summary>
public class WebhookTargetDiscovery : IWebhookTargetDiscovery
{
    private const string MetadataQuery = "__temporal_workflow_metadata";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly ITemporalGatewayFactory _temporalGatewayFactory;
    private readonly ILogger<WebhookTargetDiscovery> _logger;

    public WebhookTargetDiscovery(
        IFlowDefinitionRepository flowDefinitionRepository,
        ITemporalGatewayFactory temporalGatewayFactory,
        ILogger<WebhookTargetDiscovery> logger)
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _temporalGatewayFactory = temporalGatewayFactory;
        _logger = logger;
    }

    public async Task<List<WebhookTargetWorkflow>> DiscoverAsync(AgentActivation activation, CancellationToken cancellationToken = default)
    {
        var definitions = await _flowDefinitionRepository.GetByNameAsync(activation.AgentName, activation.TenantId) ?? new();
        var flows = definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.WorkflowType))
            .GroupBy(d => ToFlowName(d.WorkflowType), StringComparer.Ordinal)
            .Select(g => (Flow: g.Key, Activable: g.Any(d => d.Activable)))
            .OrderByDescending(f => f.Activable)
            .ThenBy(f => f.Flow, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (flows.Count == 0)
        {
            return new();
        }

        var client = await _temporalGatewayFactory.GetClientAsync(activation.AgentName);
        var results = await Task.WhenAll(flows.Select(flow =>
            DescribeAsync(client, activation, flow.Flow, flow.Activable, cancellationToken)));
        return results.ToList();
    }

    private async Task<WebhookTargetWorkflow> DescribeAsync(
        ITemporalClient client, AgentActivation activation, string flow, bool activable, CancellationToken cancellationToken)
    {
        var workflowId = WorkflowIdentifier.BuildWorkflowId(activation.TenantId, activation.AgentName, flow, activation.Name);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        try
        {
            var metadata = await client.GetWorkflowHandle(workflowId).QueryAsync<WorkflowMetadata>(
                MetadataQuery,
                Array.Empty<object?>(),
                new WorkflowQueryOptions { Rpc = new RpcOptions { CancellationToken = timeout.Token } });

            var methods = (metadata.Definition?.UpdateDefinitions ?? new())
                .Where(u => !string.IsNullOrEmpty(u.Name) && !u.Name.StartsWith("__", StringComparison.Ordinal))
                .Select(u => new WebhookTargetMethod(u.Name, string.IsNullOrWhiteSpace(u.Description) ? null : u.Description))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new WebhookTargetWorkflow(flow, workflowId, activable, WebhookTargetStatus.Running,
                methods.Count == 0 ? "This workflow has no update methods a webhook can call." : null, methods);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return new WebhookTargetWorkflow(flow, workflowId, activable, WebhookTargetStatus.NotRunning,
                activable
                    ? "Not running for this activation. Activate it to list its methods."
                    : "Not started for this activation, so its methods can't be listed.",
                new());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Could not read update methods of workflow {WorkflowId}", LogSanitizer.Sanitize(workflowId));
            return new WebhookTargetWorkflow(flow, workflowId, activable, WebhookTargetStatus.Unavailable,
                "The agent didn't answer. Is its worker running?", new());
        }
    }

    /// <summary><c>Agent Name:Flow Name</c> → <c>Flow Name</c> (agent names can't contain ':').</summary>
    public static string ToFlowName(string workflowType)
    {
        var separator = workflowType.IndexOf(':');
        return separator >= 0 ? workflowType[(separator + 1)..] : workflowType;
    }
}
