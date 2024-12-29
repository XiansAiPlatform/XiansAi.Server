using System.Text.Json;
using Temporalio.Client;

public class WorkflowRequest
{
    public required string WorkflowType { get; set; }
    public string[]? Parameters { get; set; }
}

/* 
    curl -X POST http://localhost:5257/api/workflows \
        -H "Content-Type: application/json" \
        -d '{
            "WorkflowType": "ProspectingWorkflow",
            "Parameters": ["https://www.shifter.no/nyheter/", ""]
        }'
  */
public class WorkflowStarterEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowStarterEndpoint> _logger;

    public WorkflowStarterEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowStarterEndpoint> logger)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles the HTTP request to start a workflow.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>An IResult representing the HTTP response.</returns>
    public async Task<IResult> HandleStartWorkflow(HttpContext context)
    {
        _logger.LogInformation("Received workflow start request at: {Time}", DateTime.UtcNow);
        
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var requestBody = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<WorkflowRequest>(requestBody);

            if (!ValidateRequest(request))
                return Results.BadRequest("Invalid request payload. Expected a JSON object with a WorkflowType and Input properties.");

            var workflowId = GenerateWorkflowId(request!.WorkflowType!);
            var options = CreateWorkflowOptions(workflowId);
            
            var handle = await StartWorkflowAsync(request, options);
            
            _logger.LogInformation("Successfully started workflow with ID: {WorkflowId}", workflowId);
            return Results.Ok(new { message = "Workflow started successfully", workflowId = handle.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow");
            return Results.Problem(
                title: "Workflow start failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    private static string GenerateWorkflowId(string workflowType) =>
        $"{workflowType.Replace(" ", "-")}-{Guid.NewGuid()}";

    private WorkflowOptions CreateWorkflowOptions(string workflowId) =>
        new()
        {
            TaskQueue = "DefaultQueue",
            Id = workflowId
        };

    private async Task<WorkflowHandle> StartWorkflowAsync(
        WorkflowRequest request,
        WorkflowOptions options)
    {
        var client = await _clientService.GetClientAsync();
        return await client.StartWorkflowAsync(
            request.WorkflowType!,
            request.Parameters ?? Array.Empty<string>(),
            options
        );
    }

    private static bool ValidateRequest(WorkflowRequest? request) =>
        request != null && !string.IsNullOrEmpty(request.WorkflowType);
}