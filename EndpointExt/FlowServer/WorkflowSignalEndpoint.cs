using XiansAi.Server.Temporal;
using Temporalio.Client;
using XiansAi.Server.Auth;
using System.Text.Json;

namespace XiansAi.Server.EndpointExt.WebClient;

public class WorkflowSignalRequest
{
    public required string WorkflowId { get; set; }
    public required string SignalName { get; set; }
    public object? SignalPayload { get; set; }
}

/* 
    curl -X POST http://localhost:5257/api/workflows \
        -H "Content-Type: application/json" \
        -d '{
            "WorkflowType": "ProspectingWorkflow",
            "Parameters": ["https://www.shifter.no/nyheter/", ""]
        }'
  */
public class WorkflowSignalEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowSignalEndpoint> _logger;
    private readonly ITenantContext _tenantContext;
    public WorkflowSignalEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowSignalEndpoint> logger,
        ITenantContext tenantContext    )
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Handles the HTTP request to send a signal to an existing workflow.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>An IResult representing the HTTP response.</returns>
    public async Task<IResult> HandleSignalWorkflow(WorkflowSignalRequest request)
    {
        _logger.LogInformation("Received workflow signal request at: {Time}", DateTime.UtcNow);
        
        try
        {
            if (!ValidateRequest(request))
                return Results.BadRequest("Invalid request payload. Expected a JSON object with WorkflowId and Signal properties.");

            var client = _clientService.GetClient();
            var handle = client.GetWorkflowHandle(request.WorkflowId);
            
            // Signal name is determined by the type of the signal object
            var signalName = request.SignalName;
            var signalPayload = request.SignalPayload != null 
                ? new object[] { request.SignalPayload }
                : Array.Empty<object>();
            
            await handle.SignalAsync(signalName, signalPayload);
            
            _logger.LogInformation("Successfully sent signal {SignalName} to workflow: {WorkflowId}", 
                signalName, request.WorkflowId);
            return Results.Ok(new { message = "Signal sent successfully", workflowId = request.WorkflowId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending workflow signal");
            return Results.Problem(
                title: "Workflow signal failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    private static bool ValidateRequest(WorkflowSignalRequest? request) =>
        request != null && !string.IsNullOrEmpty(request.WorkflowId);
}