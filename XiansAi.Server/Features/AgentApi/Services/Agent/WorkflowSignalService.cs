using XiansAi.Server.Temporal;
using Features.Shared.Auth;

namespace Features.AgentApi.Services.Agent;

/// <summary>
/// Represents a request to signal a Temporal workflow.
/// </summary>
public class WorkflowSignalRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow to signal.
    /// Required.
    /// </summary>
    public required string WorkflowId { get; set; }
    
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

/// <summary>
/// Handles API endpoints for signaling Temporal workflows.
/// </summary>
public class WorkflowSignalService
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowSignalService> _logger;
    private readonly ITenantContext _tenantContext;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSignalEndpoint"/> class.
    /// </summary>
    /// <param name="clientService">The service for obtaining Temporal clients.</param>
    /// <param name="logger">The logger for recording operational information.</param>
    /// <param name="tenantContext">The context providing tenant information.</param>
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
    public async Task<IResult> HandleSignalWorkflow(WorkflowSignalRequest request)
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
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var handle = client.GetWorkflowHandle(request.WorkflowId);
            
            var signalPayload = request.Payload != null 
                ? new object[] { request.Payload }
                : Array.Empty<object>();
            
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
                
            return Results.Problem(
                title: "Workflow signal failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
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
}