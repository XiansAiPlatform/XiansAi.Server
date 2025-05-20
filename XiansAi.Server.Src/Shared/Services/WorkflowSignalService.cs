using XiansAi.Server.Temporal;
using Shared.Auth;
using Shared.Utils;
using System.Text.Json.Serialization;

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

    [JsonPropertyName("EventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("Payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("SourceWorkflowId")]
    public string? SourceWorkflowId { get; set; }

    [JsonPropertyName("SourceWorkflowType")]
    public string? SourceWorkflowType { get; set; }

    [JsonPropertyName("SourceAgent")]
    public required string SourceAgent { get; set; }

    [JsonPropertyName("TargetWorkflowId")]
    public string? TargetWorkflowId { get; set; }

    [JsonPropertyName("TargetWorkflowType")]
    public required string TargetWorkflowType { get; set; }

    [JsonPropertyName("Timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Handles API endpoints for signaling Temporal workflows.
/// </summary>
public interface IWorkflowSignalService
{
    Task<IResult> SignalWorkflow(WorkflowSignalRequest request);
    Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request);
}

public class WorkflowSignalService : IWorkflowSignalService
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowSignalService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSignalService"/> class.
    /// </summary>
    /// <param name="clientService">The service for obtaining Temporal clients.</param>
    /// <param name="logger">The logger for recording operational information.</param>
    /// <param name="tenantContext">The tenant context for the current request.</param>
    /// <exception cref="ArgumentNullException">Thrown when any of the required services is null.</exception>
    public WorkflowSignalService(
        ITemporalClientService clientService,
        ILogger<WorkflowSignalService> logger,
        ITenantContext tenantContext)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }


    /// <summary>
    /// Handles the HTTP request to send a signal to an existing Temporal workflow.
    /// </summary>
    /// <param name="request">The workflow signal request containing the workflow ID, signal name, and optional payload.</param>
    /// <returns>
    /// HTTP response with one of the following status codes:
    /// - 200 OK: Signal successfully sent
    /// - 400 Bad Request: Invalid or missing required parameters
    /// - 503 Service Unavailable: Temporal client unavailable
    /// - 500 Internal Server Error: Unexpected error during processing
    /// </returns>
    public async Task<IResult> SignalWorkflow(WorkflowSignalRequest request)
    {
        _logger.LogInformation("Received workflow signal request for workflow {WorkflowId} with signal {SignalName} at: {Time}", 
            request.WorkflowId, request.SignalName, DateTime.UtcNow);

        if (request == null)
        {
            _logger.LogWarning("Received null workflow signal request");
            return Results.BadRequest("Request cannot be null");
        }

        _logger.LogInformation("Received workflow signal request for workflow {WorkflowId} with signal {SignalName} at: {Time}", 
            request.WorkflowId, request.SignalName, DateTime.UtcNow);
        
        try
        {
            if (!ValidateRequest(request))
            {
                _logger.LogWarning("Invalid workflow signal request: {WorkflowId}, {SignalName}", 
                    request.WorkflowId, request.SignalName);
                return Results.BadRequest("Invalid request. WorkflowId and SignalName are required.");
            }

            var client = _clientService.GetClient();
            if (client == null)
            {
                _logger.LogError("Failed to get Temporal client");
                throw new Exception("Failed to get Temporal client");
            }

            var handle = client.GetWorkflowHandle(request.WorkflowId);
            
            var signalPayload = request.Payload != null 
                ? new object[] { request.Payload }
                : Array.Empty<object>();

            _logger.LogInformation("Sending signal {SignalName} to workflow {WorkflowId}", 
                request.SignalName, request.WorkflowId);

            await handle.SignalAsync(request.SignalName, signalPayload);
            
            _logger.LogInformation("Successfully sent signal {SignalName} to workflow: {WorkflowId}", 
                request.SignalName, request.WorkflowId);
            
            return Results.Ok(new { 
                message = "Signal sent successfully", 
                workflowId = request.WorkflowId,
                signalName = request.SignalName
            });
        }
        catch (Temporalio.Exceptions.RpcException ex) when (ex.Message.Contains("workflow not found"))
        {
            _logger.LogWarning(ex, "Workflow not found: {WorkflowId}", request.WorkflowId);
            return Results.NotFound(new {
                message = $"Workflow with ID '{request.WorkflowId}' not found",
                workflowId = request.WorkflowId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowId}", 
                request.SignalName, request.WorkflowId);
                
            throw;
        }
    }

    public async Task<IResult> SignalWithStartWorkflow(WorkflowSignalWithStartRequest request)
    {
        _logger.LogInformation("Received workflow signal request for workflow {WorkflowType} with signal {SignalName} at: {Time}", 
            request.TargetWorkflowType, request.SignalName, DateTime.UtcNow);

        if (request == null)
        {
            _logger.LogWarning("Received null workflow signal request");
            return Results.BadRequest("Request cannot be null");
        }

        _logger.LogInformation("Received workflow signal request for workflow {WorkflowType} with signal {SignalName} at: {Time}", 
            request.TargetWorkflowType, request.SignalName, DateTime.UtcNow);
        
        try
        {
            if (!ValidateRequest(request))
            {
                _logger.LogWarning("Invalid workflow signal request: {WorkflowType}, {SignalName}", 
                    request.TargetWorkflowType, request.SignalName);
                return Results.BadRequest("Invalid request. WorkflowType and SignalName are required.");
            }

            var client = _clientService.GetClient();
            if (client == null)
            {
                _logger.LogError("Failed to get Temporal client");
                throw new Exception("Failed to get Temporal client");
            }


            var options = new NewWorkflowOptions(
                request.SourceAgent, 
                request.TargetWorkflowType, 
                request.TargetWorkflowId, 
                _tenantContext);
       
            var signalPayload = new object[] { request };

            if (request.SignalName == null) throw new Exception("SignalName is required");

            options.SignalWithStart(request.SignalName, signalPayload);

            _logger.LogInformation("Starting workflow {WorkflowType} with signal {SignalName}", 
                request.TargetWorkflowType, request.SignalName);

            await client.StartWorkflowAsync(request.TargetWorkflowType, new List<object>().AsReadOnly(), options);

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
            _logger.LogWarning(ex, "Workflow reference not found for type: {WorkflowType}", request.TargetWorkflowType);
            return Results.NotFound(new {
                message = $"Workflow type '{request.TargetWorkflowType}' could not be started or referenced",
                workflowType = request.TargetWorkflowType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowType}", 
                request.SignalName, request.TargetWorkflowType);
                
            throw;
        }
    }

    /// <summary>
    /// Validates that the workflow signal request contains all required fields.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>True if the request is valid; otherwise, false.</returns>
    /// <remarks>
    /// A valid request must have non-empty values for both WorkflowId and SignalName.
    /// </remarks>
    private static bool ValidateRequest(WorkflowSignalRequest request) =>
        !string.IsNullOrEmpty(request.WorkflowId) && 
        !string.IsNullOrEmpty(request.SignalName);

    private static bool ValidateRequest(WorkflowSignalWithStartRequest request) =>
        !string.IsNullOrEmpty(request.TargetWorkflowType) && 
        !string.IsNullOrEmpty(request.SignalName);
}