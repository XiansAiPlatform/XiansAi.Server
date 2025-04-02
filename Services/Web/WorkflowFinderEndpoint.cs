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
    public async Task<IResult> GetWorkflow(string workflowId, string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to retrieve workflow with empty workflowId");
            return Results.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Retrieving workflow with ID: {WorkflowId} and runId: {RunId}", workflowId, runId);
            var client = _clientService.GetClient();
            var workflowHandle = client.GetWorkflowHandle(workflowId, runId);
            var workflowDescription = await workflowHandle.DescribeAsync();
            var fetchHistory = await workflowHandle.FetchHistoryAsync();

            string currentActivity = await workflowHandle.QueryAsync<string>("GetCurrentActivity", Array.Empty<object?>());
            string lastError = await workflowHandle.QueryAsync<string>("GetLastError", Array.Empty<object?>());

            var workflow = MapWorkflowToResponse(workflowDescription, fetchHistory, lastError);

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
    public async Task<IResult> GetWorkflows(DateTime? startTime, DateTime? endTime, string? owner, string? status)
    {
        _logger.LogInformation("Retrieving workflows with filters - StartTime: {StartTime}, EndTime: {EndTime}, Owner: {Owner}, Status: {Status}",
            startTime, endTime, owner ?? "null", status ?? "null");

        try
        {
            var client = _clientService.GetClient();
            var workflows = new List<object>();
            var listQuery = BuildQuery(startTime, endTime, status, owner);

            _logger.LogDebug("Executing workflow query: {Query}", string.IsNullOrEmpty(listQuery) ? "No date filters" : listQuery);

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
            {
                var mappedWorkflow = MapWorkflowToResponse(workflow);

                if (ShouldSkipWorkflow(owner, mappedWorkflow.Owner))
                {
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
    private string BuildQuery(DateTime? startTime, DateTime? endTime, string? status, string? owner)
    {
        var queryParts = new List<string>();
        const string dateFormat = "yyyy-MM-ddTHH:mm:sszzz";

        // Add time-based filters
        if (startTime != null && endTime != null)
        {
            queryParts.Add($"ExecutionTime between '{startTime.Value.ToUniversalTime().ToString(dateFormat)}' and '{endTime.Value.ToUniversalTime().ToString(dateFormat)}'");
        }
        else if (startTime != null)
        {
            queryParts.Add($"ExecutionTime >= '{startTime.Value.ToUniversalTime().ToString(dateFormat)}'");
        }
        else if (endTime != null)
        {
            queryParts.Add($"ExecutionTime <= '{endTime.Value.ToUniversalTime().ToString(dateFormat)}'");
        }
        // Add tenantId filter
        queryParts.Add($"{Constants.TenantIdKey} = '{_tenantContext.TenantId}'");

        // Add userId filter if current owner is requested
        if (Constants.CurrentOwnerKey.Equals(owner, StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"{Constants.UserIdKey} = '{_tenantContext.LoggedInUser}'");
        }

        // Add status filter if specified
        if (!string.IsNullOrEmpty(status))
        {
            status = status.ToLower();
            // Ensure we're using the exact status values that Temporal expects
            string normalizedStatus = status switch
            {
                "running" => "Running",
                "completed" => "Completed",
                "failed" => "Failed",
                "canceled" => "Canceled",
                "terminated" => "Terminated",
                "continuedasnew" => "ContinuedAsNew",
                "timedout" => "TimedOut",
                _ => status // Use as-is if not matching any known status
            };

            queryParts.Add($"ExecutionStatus = '{normalizedStatus}'");
        }

        // Join all query parts with AND operator
        return string.Join(" and ", queryParts);
    }

    /// <summary>
    /// Determines if a workflow should be excluded based on ownership criteria.
    /// </summary>
    /// <param name="owner">The requested owner filter.</param>
    /// <param name="workflowOwner">The actual workflow owner.</param>
    /// <returns>True if the workflow should be skipped, false otherwise.</returns>
    private bool ShouldSkipWorkflow(string? owner, string? workflowOwner)
    {
        return Constants.CurrentOwnerKey.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
               _tenantContext.LoggedInUser != workflowOwner;
    }

    /// <summary>
    /// Identifies the current activity from the workflow history.
    /// </summary>
    /// <param name="workflowHistory">The workflow history to analyze.</param>
    /// <returns>The current activity if found, otherwise null.</returns>
    private Temporalio.Api.History.V1.ActivityTaskScheduledEventAttributes? IdentifyCurrentActivity(List<Temporalio.Api.History.V1.HistoryEvent> workflowHistory)
    {
        // Iterate in reverse to find the most recent unprocessed activity
        for (int i = workflowHistory.Count - 1; i >= 0; i--)
        {
            var evt = workflowHistory[i];

            if (evt.EventType.ToString() == "WorkflowExecutionCompleted" &&
                evt.WorkflowExecutionCompletedEventAttributes != null)
            {
                // If the workflow is completed, we can stop looking for activities.
                break;
            }

            if (evt.EventType.ToString() == "ActivityTaskScheduled" &&
                evt.ActivityTaskScheduledEventAttributes != null)
            {
                // Check if this activity has been processed
                bool hasBeenProcessed = workflowHistory
                    .Skip(i + 1)
                    .Any(e =>
                        (e.EventType.ToString() == "ActivityTaskScheduledStarted" &&
                         e.ActivityTaskStartedEventAttributes != null &&
                         e.ActivityTaskStartedEventAttributes.ScheduledEventId == evt.EventId) ||
                        (e.EventType.ToString() == "ActivityTaskScheduledCompleted" &&
                         e.ActivityTaskCompletedEventAttributes != null &&
                         e.ActivityTaskCompletedEventAttributes.ScheduledEventId == evt.EventId) ||
                        (e.EventType.ToString() == "ActivityTaskScheduledFailed" &&
                         e.ActivityTaskFailedEventAttributes != null &&
                         e.ActivityTaskFailedEventAttributes.ScheduledEventId == evt.EventId)
                    );

                // If not processed, return this activity as the current one
                if (!hasBeenProcessed)
                {
                    return evt.ActivityTaskScheduledEventAttributes;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a Temporal workflow execution to a client-friendly response object.
    /// </summary>
    /// <param name="workflow">The workflow execution to map.</param>
    /// <returns>A WorkflowResponse containing the mapped data.</returns>
    private WorkflowResponse MapWorkflowToResponse(WorkflowExecution workflow, Temporalio.Common.WorkflowHistory? fetchHistory = null, string? lastError = null)
    {
        var tenantId = ExtractMemoValue(workflow.Memo, Constants.TenantIdKey);
        var userId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);
        var agent = ExtractMemoValue(workflow.Memo, Constants.AgentKey) ?? workflow.WorkflowType;
        var assignment = ExtractMemoValue(workflow.Memo, Constants.AssignmentKey) ?? Constants.DefaultAssignment;
        Temporalio.Api.History.V1.ActivityTaskScheduledEventAttributes? currentActivity = null;

        if (fetchHistory != null)
        {
            var eventsList = fetchHistory.Events.ToList(); // Convert to a list for indexing
            // foreach (var historyEvent in eventsList)
            // {
            //     //print full object
            //     var eventObject = historyEvent.EventType.ToString();
            //     _logger.LogDebug(eventObject);
            // }

            // Assume eventsList is your list of history events.
            currentActivity = IdentifyCurrentActivity(eventsList);

            if (currentActivity != null)
            {
                Console.WriteLine($"Current activity: Type = {currentActivity.ActivityType?.Name}, ActivityId = {currentActivity.ActivityId}");
            }
            else
            {
                Console.WriteLine("No current (pending or running) activity found.");
            }
        }


        return new WorkflowResponse
        {
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
            Owner = userId,
            HistoryLength = workflow.HistoryLength,
            CurrentActivity = currentActivity,
            LastError = lastError,
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

    /// <summary>
    /// Gets or sets the history length of the workflow.
    /// </summary>
    public int HistoryLength { get; set; }

    /// <summary>
    /// Gets or sets the current activity associated with the workflow.
    /// </summary>
    public object? CurrentActivity { get; set; }
    
    /// <summary>
    /// Gets or sets the last error encountered during workflow execution.
    /// </summary>
    public string? LastError { get; set; }

}
