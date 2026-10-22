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
    /// <summary>The workflow answered and its update methods are listed.</summary>
    [JsonStringEnumMemberName("running")] Running,

    /// <summary>The workflow is not running for this activation, so its methods can't be listed.</summary>
    [JsonStringEnumMemberName("notRunning")] NotRunning,

    /// <summary>The workflow exists but did not answer (the agent is stopped, or it timed out).</summary>
    [JsonStringEnumMemberName("unavailable")] Unavailable
}

public sealed record WebhookTargetMethod(string Name, string? Description);

/// <summary>
/// One of the agent's workflows that a webhook eval can call.
/// WorkflowType is the flow name without the agent name.
/// Methods are its update methods. IsBuiltIn is true for built-in workflows.
/// Webhooks are the built-in webhook names added for this activation in Connections.
/// </summary>
public sealed record WebhookTargetWorkflow(
    string WorkflowType,
    string WorkflowId,
    bool Activable,
    WebhookTargetStatus Status,
    string? Detail,
    List<WebhookTargetMethod> Methods)
{
    public bool IsBuiltIn { get; init; }
    public List<string> Webhooks { get; init; } = new();
}

public interface IWebhookTargetDiscovery
{
    Task<List<WebhookTargetWorkflow>> DiscoverAsync(AgentActivation activation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Lists the workflows and webhooks that a webhook eval can call for an activation.
/// Update method names are read from the running workflow, so they only show while the agent is running.
/// </summary>
public class WebhookTargetDiscovery : IWebhookTargetDiscovery
{
    private const string MetadataQuery = "__temporal_workflow_metadata";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly ITemporalGatewayFactory _temporalGatewayFactory;
    private readonly IAppIntegrationService _appIntegrationService;
    private readonly ILogger<WebhookTargetDiscovery> _logger;

    public WebhookTargetDiscovery(
        IFlowDefinitionRepository flowDefinitionRepository,
        ITemporalGatewayFactory temporalGatewayFactory,
        IAppIntegrationService appIntegrationService,
        ILogger<WebhookTargetDiscovery> logger)
    {
        _flowDefinitionRepository = flowDefinitionRepository;
        _temporalGatewayFactory = temporalGatewayFactory;
        _appIntegrationService = appIntegrationService;
        _logger = logger;
    }

    public async Task<List<WebhookTargetWorkflow>> DiscoverAsync(AgentActivation activation, CancellationToken cancellationToken = default)
    {
        var definitions = await _flowDefinitionRepository.GetByNameAsync(activation.AgentName, activation.TenantId) ?? new();
        var flows = definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.WorkflowType))
            .GroupBy(d => ToFlowName(d.WorkflowType), StringComparer.Ordinal)
            .Select(g => (Flow: g.Key, Activable: g.Any(d => d.Activable), IsBuiltIn: g.Any(d => d.IsBuiltIn)))
            .OrderByDescending(f => f.Activable)
            .ThenBy(f => f.Flow, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (flows.Count == 0)
        {
            return new();
        }

        var webhooksByFlow = await GetBuiltinWebhooksAsync(activation);
        var client = await _temporalGatewayFactory.GetClientAsync(activation.AgentName);
        var results = await Task.WhenAll(flows.Select(async flow =>
            (await DescribeAsync(client, activation, flow.Flow, flow.Activable, cancellationToken)) with
            {
                IsBuiltIn = flow.IsBuiltIn,
                Webhooks = webhooksByFlow.TryGetValue(flow.Flow, out var names) ? names : new(),
            }));
        return results.ToList();
    }

    /// <summary>
    /// The built-in webhooks added for this activation in Connections, grouped by workflow.
    /// If this fails, an empty list is returned and the user can type the name.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> GetBuiltinWebhooksAsync(AgentActivation activation)
    {
        try
        {
            var result = await _appIntegrationService.GetBuiltinWebhooksAsync(activation.TenantId, activation.Name, activation.AgentName);
            if (!result.IsSuccess || result.Data == null)
            {
                return new();
            }

            return result.Data
                .Where(i => i.IsEnabled)
                .Select(i => (
                    Flow: Convert.ToString(i.Configuration.GetValueOrDefault("workflowName")),
                    Name: Convert.ToString(i.Configuration.GetValueOrDefault("webhookName"))))
                .Where(w => !string.IsNullOrWhiteSpace(w.Flow) && !string.IsNullOrWhiteSpace(w.Name))
                .GroupBy(w => w.Flow!, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(w => w.Name!).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not list builtin webhooks for activation {ActivationId}", LogSanitizer.Sanitize(activation.Id));
            return new();
        }
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

    /// <summary>Turns <c>Agent Name:Flow Name</c> into <c>Flow Name</c>.</summary>
    public static string ToFlowName(string workflowType)
    {
        var separator = workflowType.IndexOf(':');
        return separator >= 0 ? workflowType[(separator + 1)..] : workflowType;
    }
}
