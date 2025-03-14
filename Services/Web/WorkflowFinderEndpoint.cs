using Temporalio.Client;
using Temporalio.Converters;
using XiansAi.Server.Auth;
using XiansAi.Server.Temporal;

namespace XiansAi.Server.Services.Web;

/// <summary>
/// Endpoint for retrieving and managing workflow information from Temporal.
/// </summary>
public class WorkflowFinderEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowFinderEndpoint> _logger;
    private readonly ITenantContext _tenantContext;

    private const string TenantIdKey = "tenantId";
    private const string UserIdKey = "userId";
    private const string AgentKey = "agent";
    private const string AssignmentKey = "assignment";
    private const string CurrentOwner = "current";

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowFinderEndpoint"/> class.
    /// </summary>
    /// <param name="clientService">The Temporal client service for workflow operations.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="tenantContext">Context containing tenant-specific information.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WorkflowFinderEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowFinderEndpoint> logger,
        ITenantContext tenantContext)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Retrieves a specific workflow by its ID.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow.</param>
    /// <returns>A result containing the workflow details if found, or an error response.</returns>
    public async Task<IResult> GetWorkflow(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to retrieve workflow with empty workflowId");
            return Results.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Retrieving workflow with ID: {WorkflowId}", workflowId);
            var client = _clientService.GetClient();
            var workflowHandle = client.GetWorkflowHandle(workflowId);
            var workflowDescription = await workflowHandle.DescribeAsync();

            var workflow = MapWorkflowToResponse(workflowDescription);
            _logger.LogInformation("Successfully retrieved workflow {WorkflowId} of type {WorkflowType}", 
                workflow.WorkflowId, workflow.WorkflowType);
            return Results.Ok(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflow {WorkflowId}. Error: {ErrorMessage}", 
                workflowId, ex.Message);
            return Results.Problem(
                title: "Failed to retrieve workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Retrieves a list of workflows based on specified filters.
    /// </summary>
    /// <param name="startTime">Optional start time filter for workflow execution.</param>
    /// <param name="endTime">Optional end time filter for workflow execution.</param>
    /// <param name="owner">Optional owner filter for workflows.</param>
    /// <returns>A result containing the list of filtered workflows.</returns>
    public async Task<IResult> GetWorkflows(DateTime? startTime, DateTime? endTime, string? owner)
    {
        _logger.LogInformation("Retrieving workflows with filters - StartTime: {StartTime}, EndTime: {EndTime}, Owner: {Owner}", 
            startTime, endTime, owner ?? "null");

        try
        {
            var client = _clientService.GetClient();
            var workflows = new List<object>();
            var listQuery = BuildDateRangeQuery(startTime, endTime);

            _logger.LogDebug("Executing workflow query: {Query}", string.IsNullOrEmpty(listQuery) ? "No date filters" : listQuery);

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
            {
                var mappedWorkflow = MapWorkflowToResponse(workflow);
                
                if (ShouldSkipWorkflow(owner, mappedWorkflow.Owner))
                {
                    _logger.LogDebug("Skipping workflow {WorkflowId} due to owner filter", mappedWorkflow.Id);
                    continue;
                }

                workflows.Add(mappedWorkflow);
            }

            _logger.LogInformation("Retrieved {Count} workflows matching the specified criteria", workflows.Count);
            return Results.Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflows. Error: {ErrorMessage}", ex.Message);
            return Results.Problem(
                title: "Failed to retrieve workflows",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    /// <summary>
    /// Builds a date range query string for filtering workflows.
    /// </summary>
    /// <param name="startTime">The start time of the range.</param>
    /// <param name="endTime">The end time of the range.</param>
    /// <returns>A query string for temporal workflow filtering.</returns>
    private string BuildDateRangeQuery(DateTime? startTime, DateTime? endTime)
    {
        if (startTime == null && endTime == null)
        {
            return string.Empty;
        }

        const string dateFormat = "yyyy-MM-ddTHH:mm:sszzz";
        
        if (startTime != null && endTime != null)
        {
            return $"ExecutionTime between '{startTime.Value.ToUniversalTime().ToString(dateFormat)}' and '{endTime.Value.ToUniversalTime().ToString(dateFormat)}'";
        }
        
        if (startTime != null)
        {
            return $"ExecutionTime >= '{startTime.Value.ToUniversalTime().ToString(dateFormat)}'";
        }
        
        return $"ExecutionTime <= '{endTime!.Value.ToUniversalTime().ToString(dateFormat)}'";
    }

    /// <summary>
    /// Determines if a workflow should be excluded based on ownership criteria.
    /// </summary>
    /// <param name="owner">The requested owner filter.</param>
    /// <param name="workflowOwner">The actual workflow owner.</param>
    /// <returns>True if the workflow should be skipped, false otherwise.</returns>
    private bool ShouldSkipWorkflow(string? owner, string? workflowOwner)
    {
        return CurrentOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) && 
               _tenantContext.LoggedInUser != workflowOwner;
    }

    /// <summary>
    /// Maps a Temporal workflow execution to a client-friendly response object.
    /// </summary>
    /// <param name="workflow">The workflow execution to map.</param>
    /// <returns>A WorkflowResponse containing the mapped data.</returns>
    private WorkflowResponse MapWorkflowToResponse(WorkflowExecution workflow)
    {
        _logger.LogDebug("Mapping workflow {WorkflowId} to response object", workflow.Id);
        var tenantId = ExtractMemoValue(workflow.Memo, TenantIdKey);
        var userId = ExtractMemoValue(workflow.Memo, UserIdKey);
        var agent = ExtractMemoValue(workflow.Memo, AgentKey);
        var assignment = ExtractMemoValue(workflow.Memo, AssignmentKey);

        return new WorkflowResponse
        {
            Id = workflow.Id,
            Agent = agent,
            Assignment = assignment,
            ParentId = workflow.ParentId,
            ParentRunId = workflow.ParentRunId,
            WorkflowId = workflow.Id,
            RunId = workflow.RunId,
            WorkflowType = workflow.WorkflowType,
            Status = workflow.Status.ToString(),
            StartTime = workflow.StartTime,
            ExecutionTime = workflow.ExecutionTime,
            CloseTime = workflow.CloseTime,
            TenantId = tenantId,
            Owner = userId
        };
    }

    /// <summary>
    /// Extracts a value from the workflow memo dictionary.
    /// </summary>
    /// <param name="memo">The memo dictionary containing workflow metadata.</param>
    /// <param name="key">The key to extract.</param>
    /// <returns>The extracted string value, or null if not found.</returns>
    private string? ExtractMemoValue(IReadOnlyDictionary<string, IEncodedRawValue> memo, string key)
    {
        if (memo.TryGetValue(key, out var memoValue))
        {
            return memoValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
        }
        return null;
    }
}

/// <summary>
/// Represents a workflow response object containing workflow execution details.
/// </summary>
public class WorkflowResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the workflow.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the agent associated with the workflow.
    /// </summary>
    public string? Agent { get; set; }

    /// <summary>
    /// Gets or sets the assignment details for the workflow.
    /// </summary>
    public string? Assignment { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier associated with the workflow.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the owner of the workflow.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the workflow identifier.
    /// </summary>
    public string? WorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the run identifier for the workflow execution.
    /// </summary>
    public string? RunId { get; set; }

    /// <summary>
    /// Gets or sets the type of the workflow.
    /// </summary>
    public string? WorkflowType { get; set; }

    /// <summary>
    /// Gets or sets the current status of the workflow.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow started.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the execution time of the workflow.
    /// </summary>
    public DateTime? ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow closed.
    /// </summary>
    public DateTime? CloseTime { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the parent workflow, if any.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the run identifier of the parent workflow, if any.
    /// </summary>
    public string? ParentRunId { get; set; }
}
