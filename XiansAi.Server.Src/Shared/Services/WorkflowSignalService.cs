using XiansAi.Server.Temporal;
using Shared.Auth;
using Newtonsoft.Json;

namespace Shared.Services;

/// <summary>
/// Represents a request to signal a Temporal workflow.
/// </summary>
public class WorkflowSignalRequestBase
{
    
    /// <summary>
    /// Gets or sets the name of the signal to send to the workflow.
    /// Required.
    /// </summary>
    public required string SignalName { get; set; }
    
    /// <summary>
    /// Gets or sets the optional payload data to include with the signal.
    /// May be null if no data needs to be sent with the signal.
    /// </summary>
    public object? Payload { get; set; }

}

public class WorkflowSignalRequest: WorkflowSignalRequestBase
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow to signal.
    /// Required.
    /// </summary>
    public required string WorkflowId { get; set; }
}

public class WorkflowSignalWithStartRequest : WorkflowSignalRequestBase
{

    /// <summary>
    /// Gets or sets the optional workflow ID to start.
    /// May be null if no workflow needs to be started.
    /// </summary>
    public required string ProposedWorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the optional workflow type to start.
    /// May be null if no workflow needs to be started.
    /// </summary>
    public required string WorkflowType { get; set; }

    /// <summary>
    /// Gets or sets the optional workflow ID to start.
    /// May be null if no workflow needs to be started.
    /// </summary>
    public string? QueueName { get; set; }

    /// <summary>
    /// Gets or sets the optional workflow ID to start.
    /// May be null if no workflow needs to be started.
    /// </summary>
    public string? Agent { get; set; }

    /// <summary>
    /// Gets or sets the optional workflow ID to start.
    /// May be null if no workflow needs to be started.
    /// </summary>
    public string? Assignment { get; set; }
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

            _logger.LogInformation("Sending signal {SignalName} to workflow {WorkflowId} with payload {Payload}", 
                request.SignalName, request.WorkflowId, JsonConvert.SerializeObject(signalPayload));

            await handle.SignalAsync(request.SignalName, signalPayload);
            
            _logger.LogInformation("Successfully sent signal {SignalName} to workflow: {WorkflowId}", 
                request.SignalName, request.WorkflowId);
            
            return Results.Ok(new { 
                message = "Signal sent successfully", 
                workflowId = request.WorkflowId,
                signalName = request.SignalName
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
            request.WorkflowType, request.SignalName, DateTime.UtcNow);

        if (request == null)
        {
            _logger.LogWarning("Received null workflow signal request");
            return Results.BadRequest("Request cannot be null");
        }

        _logger.LogInformation("Received workflow signal request for workflow {WorkflowType} with signal {SignalName} at: {Time}", 
            request.WorkflowType, request.SignalName, DateTime.UtcNow);
        
        try
        {
            if (!ValidateRequest(request))
            {
                _logger.LogWarning("Invalid workflow signal request: {WorkflowType}, {SignalName}", 
                    request.WorkflowType, request.SignalName);
                return Results.BadRequest("Invalid request. WorkflowType and SignalName are required.");
            }

            var client = _clientService.GetClient();
            if (client == null)
            {
                _logger.LogError("Failed to get Temporal client");
                throw new Exception("Failed to get Temporal client");
            }


            var options = new NewWorkflowOptions(request.ProposedWorkflowId, request.WorkflowType, _tenantContext, request.QueueName, request.Agent, request.Assignment);
       
            var signalPayload = request.Payload != null 
                ? new object[] { request.Payload }
                : Array.Empty<object>();

            options.SignalWithStart(request.SignalName, signalPayload);

            _logger.LogInformation("Starting workflow {WorkflowType} with signal {SignalName} and payload {Payload}", 
                request.WorkflowType, request.SignalName, JsonConvert.SerializeObject(signalPayload));

            await client.StartWorkflowAsync(request.WorkflowType, new List<object>().AsReadOnly(), options);

            _logger.LogInformation("Successfully started workflow {WorkflowType} with signal {SignalName} and payload {Payload}", 
                request.WorkflowType, request.SignalName, JsonConvert.SerializeObject(signalPayload));
            
            return Results.Ok(new { 
                message = "Signal with start sent successfully", 
                workflowId = request.ProposedWorkflowId,
                signalName = request.SignalName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending signal {SignalName} to workflow {WorkflowType}", 
                request.SignalName, request.WorkflowType);
                
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
        !string.IsNullOrEmpty(request.WorkflowType) && 
        !string.IsNullOrEmpty(request.SignalName);
}