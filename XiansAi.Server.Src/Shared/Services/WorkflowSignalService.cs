using Shared.Auth;
using Shared.Utils;
using System.Text.Json.Serialization;
using Shared.Utils.Temporal;
using Features.WebApi.Services;
using System.Diagnostics;
using OpenTelemetry.Trace;

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
    private readonly IAgentService _agentService;
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowSignalService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSignalService"/> class.
    /// </summary>
    /// <param name="clientFactory">The factory for obtaining Temporal clients.</param>
    /// <param name="logger">The logger for recording operational information.</param>
    /// <param name="tenantContext">The tenant context for the current request.</param>
    /// <param name="agentService">The agent service for checking if an agent is system scoped.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public WorkflowSignalService(
        IAgentService agentService,
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowSignalService> logger,
        ITenantContext tenantContext)
    {
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public async Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request)
    {
        _logger.LogInformation("[OpenTelemetry] DIAGNOSTIC: SignalWithStartWorkflow() called for workflow {WorkflowType}", request.TargetWorkflowType);
        
        // IMPORTANT: Extract trace context BEFORE creating a new activity
        // Once we create a new activity, Activity.Current changes and we lose the HTTP request context
        var currentActivity = Activity.Current;
        string? traceParent = null;
        string? traceState = null;
        
        _logger.LogInformation("[OpenTelemetry] DIAGNOSTIC: Activity.Current state during trace extraction:");
        if (currentActivity != null)
        {
            _logger.LogInformation("  - Activity.Current EXISTS");
            _logger.LogInformation("  - TraceId: {TraceId}", currentActivity.TraceId);
            _logger.LogInformation("  - SpanId: {SpanId}", currentActivity.SpanId);
            _logger.LogInformation("  - ParentSpanId: {ParentSpanId}", currentActivity.ParentSpanId);
            _logger.LogInformation("  - OperationName: {OperationName}", currentActivity.OperationName);
            _logger.LogInformation("  - Source.Name: {SourceName}", currentActivity.Source.Name);
            
            // Format traceparent header: 00-{TraceId}-{SpanId}-{Flags}
            traceParent = $"00-{currentActivity.TraceId}-{currentActivity.SpanId}-{(currentActivity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "01" : "00")}";
            traceState = currentActivity.TraceStateString;
            
            _logger.LogInformation("[OpenTelemetry] Extracted trace context: TraceId={TraceId}, SpanId={SpanId}, TraceParent={TraceParent}", 
                currentActivity.TraceId, currentActivity.SpanId, traceParent);
        }
        else
        {
            _logger.LogWarning("[OpenTelemetry] WARNING: Activity.Current is NULL - cannot propagate trace context to Temporal workflow");
            _logger.LogWarning("  - This means the HTTP request did not create an activity");
            _logger.LogWarning("  - Or Activity.Current was lost before reaching this point");
        }
        
        // Create a span for the Temporal workflow operation (after extracting trace context)
        using var activity = ActivitySource.StartActivity(
            "Temporal.SignalWithStart",
            ActivityKind.Client);
        
        try
        {
            // Add operation tags
            if (activity != null)
            {
                activity.SetTag("temporal.operation", "signal_with_start");
                activity.SetTag("temporal.workflow_type", request.TargetWorkflowType);
                activity.SetTag("temporal.signal_name", request.SignalName ?? "unknown");
                activity.SetTag("temporal.source_agent", request.SourceAgent);
                activity.SetTag("temporal.workflow_id", request.TargetWorkflowId ?? "auto-generated");
                
                // Add tenant context
                if (!string.IsNullOrEmpty(_tenantContext.TenantId))
                {
                    activity.SetTag("tenant.id", _tenantContext.TenantId);
                }
                
                // Add user context if available
                if (!string.IsNullOrEmpty(_tenantContext.LoggedInUser))
                {
                    activity.SetTag("user.id", _tenantContext.LoggedInUser);
                }
            }
            var client = await _clientFactory.GetClientAsync() ?? throw new Exception("Failed to get Temporal client");
            
            // Add client connection info to span
            if (activity != null)
            {
                activity.SetTag("temporal.namespace", client.Options.Namespace);
            }

            var systemScoped = _agentService.IsSystemAgent(request.SourceAgent).Result.Data;

            var options = new NewWorkflowOptions(
                request.SourceAgent, 
                systemScoped,
                request.TargetWorkflowType, 
                request.TargetWorkflowId, 
                _tenantContext);
            
            // Add trace context to memo so workflow can restore it
            if (traceParent != null)
            {
                options.AddToMemo("traceparent", traceParent);
                if (!string.IsNullOrEmpty(traceState))
                {
                    options.AddToMemo("tracestate", traceState);
                }
                
                _logger.LogInformation("[OpenTelemetry] DIAGNOSTIC: Successfully added trace context to workflow memo:");
                _logger.LogInformation("  - traceparent: {TraceParent}", traceParent);
                _logger.LogInformation("  - tracestate: {TraceState}", traceState ?? "null");
                _logger.LogInformation("  - This memo will be accessible in the workflow signal handler");
            }
            else
            {
                _logger.LogWarning("[OpenTelemetry] WARNING: traceParent is null - NOT adding trace context to memo");
                _logger.LogWarning("  - Workflow will not be able to restore trace context");
                _logger.LogWarning("  - This will result in a disconnected trace");
            }
       
            var signalPayload = new object[] { request };

            if (request.SignalName == null) throw new Exception("SignalName is required");

            options.SignalWithStart(request.SignalName, signalPayload);

            _logger.LogInformation("Starting workflow {WorkflowType} with signal {SignalName}", 
                request.TargetWorkflowType, request.SignalName);

            await client.StartWorkflowAsync(request.TargetWorkflowType, new List<object>().AsReadOnly(), options);

            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }

            _logger.LogInformation("Successfully started workflow {WorkflowType} with signal {SignalName}", 
                request.TargetWorkflowType, request.SignalName);
            
            return Results.Ok(new { 
                message = "Signal with start sent successfully", 
                workflowId = request.TargetWorkflowId,
                signalName = request.SignalName
            });
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Message.Contains("workflow not found"))
        {
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.RecordException(ex);
            }
            
            _logger.LogWarning(ex, "Workflow reference not found for type: {WorkflowType}", request.TargetWorkflowType);
            return Results.NotFound(new {
                message = $"Workflow type '{request.TargetWorkflowType}' could not be started or referenced",
                workflowType = request.TargetWorkflowType
            });
        }
        catch (Exception ex)
        {
            if (activity != null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.RecordException(ex);
            }
            
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowType}", 
                request.SignalName, request.TargetWorkflowType);
                
            throw;
        }
    }
}