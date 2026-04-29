using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry.Trace;
using Shared.Auth;
using Shared.Utils;
using System.Text.Json.Serialization;
using Shared.Utils.Temporal;
using Features.Shared.Configuration;
using Features.WebApi.Services;

namespace Shared.Services;

public class WorkflowEventRequest
{
    public object? Payload { get; set; }
    public required string WorkflowId { get; set; }

    public WorkflowSignalRequest ToWorkflowSignalRequest()
    {
       return new WorkflowSignalRequest
       {
            SignalName = Constants.SIGNAL_INBOUND_EVENT,
            WorkflowId = WorkflowId,
            Payload = Payload
       };
    }
}

public class WorkflowSignalRequest
{
    public required string SignalName { get; set; }
    public object? Payload { get; set; }
    public required string WorkflowId { get; set; }
}

public class WorkflowSignalWithStartRequest
{
    [JsonPropertyName("SignalName")]
    public string? SignalName { get; set; }
    [JsonPropertyName("SourceAgent")]
    public required string SourceAgent { get; set; }

    [JsonPropertyName("TargetWorkflowId")]
    public string? TargetWorkflowId { get; set; }

    [JsonPropertyName("TargetWorkflowType")]
    public required string TargetWorkflowType { get; set; }

    [JsonPropertyName("Payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("SourceWorkflowId")]
    public string? SourceWorkflowId { get; set; }

    [JsonPropertyName("SourceWorkflowType")]
    public string? SourceWorkflowType { get; set; }

    [JsonPropertyName("ParticipantId")]
    public string? ParticipantId { get; set; }
}

/// <summary>
/// Handles API endpoints for signaling Temporal workflows.
/// </summary>
public interface IWorkflowSignalService
{
    Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request);
}

public class WorkflowSignalService : IWorkflowSignalService
{
    private static readonly ActivitySource ActivitySource = new("XiansAi.Server.Temporal");

    // perf: cache IsSystemAgent results — agent system-scope never changes at runtime,
    // so a process-lifetime ConcurrentDictionary avoids a MongoDB round-trip on every signal.
    private static readonly ConcurrentDictionary<string, bool> _systemAgentCache = new(StringComparer.Ordinal);

    private readonly IAgentService _agentService;
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowSignalService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly string _tenantTagName;
    private readonly bool _includeUserIdentity;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSignalService"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory for obtaining Temporal clients.</param>
    /// <param name="logger">The logger for recording operational information.</param>
    /// <param name="tenantContext">The tenant context for the current request.</param>
    /// <param name="agentService">The agent service for checking if an agent is system scoped.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public WorkflowSignalService(
        IAgentService agentService,
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowSignalService> logger,
        ITenantContext tenantContext,
        IConfiguration configuration)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _tenantTagName = OpenTelemetryExtensions.ResolveTenantTagName(
            configuration ?? throw new ArgumentNullException(nameof(configuration)));
        _includeUserIdentity = configuration.GetValue<bool>("OpenTelemetry:IncludeUserIdentity", false);
    }

    public async Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request)
    {
        using var activity = ActivitySource.StartActivity("Temporal.SignalWithStart", ActivityKind.Client);

        try
        {
            activity?.SetTag("temporal.operation", "signal_with_start");
            activity?.SetTag("temporal.workflow_type", request.TargetWorkflowType);
            activity?.SetTag("temporal.signal_name", request.SignalName ?? "unknown");
            activity?.SetTag("temporal.source_agent", request.SourceAgent);
            activity?.SetTag("temporal.workflow_id", request.TargetWorkflowId ?? "auto-generated");

            if (!string.IsNullOrEmpty(_tenantContext.TenantId))
            {
                activity?.SetTag(_tenantTagName, _tenantContext.TenantId);
            }

            if (_includeUserIdentity && !string.IsNullOrEmpty(_tenantContext.LoggedInUser))
            {
                activity?.SetTag("user.id", _tenantContext.LoggedInUser);
            }

            var client = await _clientFactory.GetClientAsync() ?? throw new Exception("Failed to get Temporal client");

            activity?.SetTag("temporal.namespace", client.Options.Namespace);

            // perf: serve IsSystemAgent from the process-lifetime cache to avoid a MongoDB
            // round-trip on every incoming conversation message signal.
            var systemScoped = await GetSystemScopedCachedAsync(request.SourceAgent);

            var options = new NewWorkflowOptions(
                request.SourceAgent,
                systemScoped,
                request.TargetWorkflowType,
                request.TargetWorkflowId,
                _tenantContext,
                string.IsNullOrWhiteSpace(request.ParticipantId) ? null : request.ParticipantId);

            var signalPayload = new object[] { request };

            if (request.SignalName == null) throw new Exception("SignalName is required");

            options.SignalWithStart(request.SignalName, signalPayload);

            _logger.LogInformation("Starting/invoking workflow `{WorkflowId}` with signal `{SignalName}`",
                request.TargetWorkflowId, request.SignalName);

            await client.StartWorkflowAsync(request.TargetWorkflowType, new List<object>().AsReadOnly(), options);

            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation("Successfully invoked workflow type {WorkflowType} with signal {SignalName}",
                request.TargetWorkflowType, request.SignalName);

            return Results.Ok(new {
                message = "Signal with start sent successfully",
                workflowId = request.TargetWorkflowId,
                signalName = request.SignalName
            });
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Message.Contains("workflow not found"))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            _logger.LogWarning(ex, "Workflow reference not found for type: {WorkflowType}", request.TargetWorkflowType);
            return Results.NotFound(new {
                message = $"Workflow type '{request.TargetWorkflowType}' could not be started or referenced",
                workflowType = request.TargetWorkflowType
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowType}",
                request.SignalName, request.TargetWorkflowType);

            throw;
        }
    }

    private async Task<bool> GetSystemScopedCachedAsync(string agentName)
    {
        if (_systemAgentCache.TryGetValue(agentName, out var cached))
        {
            return cached;
        }

        var result = (await _agentService.IsSystemAgent(agentName)).Data;
        _systemAgentCache[agentName] = result;
        return result;
    }
}
