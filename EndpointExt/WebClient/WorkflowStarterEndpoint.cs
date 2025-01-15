using XiansAi.Server.Temporal;
using Temporalio.Client;
using XiansAi.Server.Auth;
using System.Text.Json;

namespace XiansAi.Server.EndpointExt.WebClient;

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
    private readonly ITenantContext _tenantContext;
    public WorkflowStarterEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowStarterEndpoint> logger,
        ITenantContext tenantContext    )
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Handles the HTTP request to start a workflow.
    /// </summary>
    /// <param name="context">The HTTP context containing the request.</param>
    /// <returns>An IResult representing the HTTP response.</returns>
    public async Task<IResult> HandleStartWorkflow(WorkflowRequest request)
    {
        _logger.LogInformation("Received workflow start request at: {Time}", DateTime.UtcNow);
        
        try
        {
            if (!ValidateRequest(request))
                return Results.BadRequest("Invalid request payload. Expected a JSON object with a WorkflowType and Input properties.");

            var workflowId = GenerateWorkflowId(request.WorkflowType);
            var options = CreateWorkflowOptions(workflowId, request.WorkflowType);
            
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

    private string GenerateWorkflowId(string workflowType) =>
        $"{workflowType.Replace(" ", "")}--{_tenantContext.LoggedInUser}--{Guid.NewGuid()}";

    private WorkflowOptions CreateWorkflowOptions(string workflowId, string workFlowType) =>
        new()
        {
            TaskQueue = workFlowType,
            Id = workflowId,
            Memo = new Dictionary<string, object> { 
                { "tenantId", _tenantContext.TenantId },
                { "userId", _tenantContext.LoggedInUser ?? "-unknown-" }
            }
        };

    private async Task<WorkflowHandle> StartWorkflowAsync(
        WorkflowRequest request,
        WorkflowOptions options)
    {
        _logger.LogDebug("Starting workflow {workflowType} with options {options}", request.WorkflowType, JsonSerializer.Serialize(options));
        var client = _clientService.GetClient();
        return await client.StartWorkflowAsync(
            request.WorkflowType!,
            request.Parameters ?? Array.Empty<string>(),
            options
        );
    }

    private static bool ValidateRequest(WorkflowRequest? request) =>
        request != null && !string.IsNullOrEmpty(request.WorkflowType);
}