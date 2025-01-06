using XiansAi.Server.Temporal;

namespace XiansAi.Server.EndpointExt.WebClient;

public class WorkflowFinderEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowFinderEndpoint> _logger;

    public WorkflowFinderEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowFinderEndpoint> logger)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IResult> GetWorkflow(string workflowId)
    {
        var client = await _clientService.GetClientAsync();
        var workflowHandle = client.GetWorkflowHandle(workflowId);
        var workflowDescription = await workflowHandle.DescribeAsync();

        var workflow = new
        {
            workflowDescription.Id,
            workflowDescription.RunId,
            workflowDescription.WorkflowType,
            Status = workflowDescription.Status.ToString(),
            workflowDescription.StartTime,
            workflowDescription.ExecutionTime,
            workflowDescription.CloseTime,
            workflowDescription.ParentId,
            workflowDescription.ParentRunId,
        };
        return Results.Ok(workflow);
    }

    public async Task<IResult> GetWorkflows()
    {
        _logger.LogInformation("Getting list of workflows at: {Time}", DateTime.UtcNow);

        try
        {
            var client = await _clientService.GetClientAsync();
            var workflows = new List<object>();

            await foreach (var workflow in client.ListWorkflowsAsync(""))
            {
                workflows.Add(new
                {
                    workflow.Id,
                    workflow.WorkflowType,
                    workflow.RunId,
                    Status = workflow.Status.ToString(),
                    workflow.StartTime,
                    workflow.ExecutionTime,
                    workflow.CloseTime
                });
            }

            _logger.LogInformation("Successfully retrieved {Count} workflows", workflows.Count);
            return Results.Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return Results.Problem(
                title: "Failed to retrieve workflows",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }
}
