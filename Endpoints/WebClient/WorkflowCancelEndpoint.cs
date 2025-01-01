

public class WorkflowCancelEndpoint
{
    private readonly ITemporalClientService _temporalClientService;
    private readonly ILogger<WorkflowCancelEndpoint> _logger;

    public WorkflowCancelEndpoint(ITemporalClientService temporalClientService, ILogger<WorkflowCancelEndpoint> logger)
    {
        _temporalClientService = temporalClientService;
        _logger = logger;
    }

    /*
    curl -X POST "http://localhost:5000/api/workflows/{workflowId}/cancel?force=true"
    */
    public async Task<IResult> CancelWorkflow(HttpContext context)
    {
        try
        {
            _logger.LogInformation("Cancelling workflow");
            // Extract workflowId from route parameters
            var workflowId = context.Request.RouteValues["workflowId"]?.ToString();
            
            if (string.IsNullOrEmpty(workflowId))
            {
                return Results.BadRequest("WorkflowId is required");
            }

            // Check for 'force' flag in query parameters
            var force = context.Request.Query.ContainsKey("force") && 
                        bool.TryParse(context.Request.Query["force"], out var forceValue) && 
                        forceValue;

            var client = await _temporalClientService.GetClientAsync();
            var handle = client.GetWorkflowHandle(workflowId);
            
            if (force)
            {
                // Terminate the workflow
                await handle.TerminateAsync("Terminated by user request");
                return Results.Ok(new { message = $"Workflow {workflowId} termination requested" });
            }
            else
            {
                // Cancel the workflow
                await handle.CancelAsync();
                return Results.Ok(new { message = $"Workflow {workflowId} cancellation requested" });
            }
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling or terminating workflow");
            return Results.Problem(
                detail: $"Error canceling or terminating workflow: {ex.Message}",
                statusCode: 500
            );
        }
    }
}
