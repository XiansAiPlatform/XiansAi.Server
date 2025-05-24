using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Temporalio.Converters;
using System.Text.Json;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Api.TaskQueue.V1;
using Shared.Utils.Temporal;
using Shared.Data;
using Features.WebApi.Repositories;
using Shared.Repositories;
using Shared.Utils.Services;
using Features.WebApi.Models;
using Temporalio.Common;

namespace Features.WebApi.Services;

public interface IWorkflowFinderService
{
    Task<ServiceResult<WorkflowResponse>> GetWorkflow(string workflowId, string? runId = null);
    Task<ServiceResult<List<WorkflowResponse>>> GetWorkflows(DateTime? startTime, DateTime? endTime, string? owner, string? status);
    Task<ServiceResult<List<WorkflowResponse>>> GetRunningWorkflowsByAgentAndType(string? agentName, string? typeName);
}

/// <summary>
/// Endpoint for retrieving and managing workflow information from Temporal.
/// </summary>
public class WorkflowFinderService : IWorkflowFinderService
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowFinderService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IDatabaseService _databaseService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowFinderService"/> class.
    /// </summary>
    /// <param name="clientService">The Temporal client service for workflow operations.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="tenantContext">Context containing tenant-specific information.</param>
    /// <param name="databaseService">The database service for accessing workflow logs.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WorkflowFinderService(
        ITemporalClientService clientService,
        ILogger<WorkflowFinderService> logger,
        ITenantContext tenantContext,
        IDatabaseService databaseService)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _databaseService = databaseService;
    }

    /// <summary>
    /// Retrieves a specific workflow by its ID.
    /// </summary>
    /// <param name="workflowId">The unique identifier of the workflow.</param>
    /// <param name="workflowRunId">Optional run identifier for the workflow.</param>
    /// <returns>A result containing the workflow details if found, or an error response.</returns>
    public async Task<ServiceResult<WorkflowResponse>> GetWorkflow(string workflowId, string? workflowRunId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to retrieve workflow with empty workflowId");
            return ServiceResult<WorkflowResponse>.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Retrieving workflow with ID: {WorkflowId} and workflowRunId: {WorkflowRunId}", workflowId, workflowRunId);
            var client = _clientService.GetClient();
            var workflowHandle = client.GetWorkflowHandle(workflowId, workflowRunId);
            var workflowDescription = await workflowHandle.DescribeAsync();
            //log the workflow description object
            _logger.LogDebug("Workflow description: {Description}", JsonSerializer.Serialize(workflowDescription));
            string recentWorkerCount = await GetRecentWorkerCount(client, workflowDescription.TaskQueue!);

            var fetchHistory = await workflowHandle.FetchHistoryAsync();
            var workflow = MapWorkflowToResponse(workflowDescription, fetchHistory, recentWorkerCount, workflowDescription.TaskQueue!);

            _logger.LogInformation("Successfully retrieved workflow {WorkflowId} of type {WorkflowType}",
                workflow.WorkflowId, workflow.WorkflowType);
            return ServiceResult<WorkflowResponse>.Success(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflow {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<WorkflowResponse>.BadRequest("Failed to retrieve workflow: " + ex.Message);
        }
    }

    /// <summary>
    /// Retrieves a list of workflows based on specified filters.
    /// </summary>
    /// <param name="startTime">Optional start time filter for workflow execution.</param>
    /// <param name="endTime">Optional end time filter for workflow execution.</param>
    /// <param name="owner">Optional owner filter for workflows.</param>
    /// <param name="status">Optional status filter for workflows.</param>
    /// <returns>A result containing the list of filtered workflows.</returns>
    public async Task<ServiceResult<List<WorkflowResponse>>> GetWorkflows(DateTime? startTime, DateTime? endTime, string? owner, string? status)
    {
        _logger.LogInformation("Retrieving workflows with filters - StartTime: {StartTime}, EndTime: {EndTime}, Owner: {Owner}, Status: {Status}",
            startTime, endTime, owner ?? "null", status ?? "null");

        try
        {
            var client = _clientService.GetClient();
            var workflows = new List<WorkflowResponse>();
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

            // retrieve last logs for each workflow run
            var logRepository = new LogRepository(_databaseService);
            var logs = await logRepository.GetLastLogAsync(startTime, endTime);
            foreach (var workflow in workflows)
            {
                var workflowId = workflow.WorkflowId;
                var workflowRunId = workflow.RunId;

                var lastLog = logs.FirstOrDefault(x => x.WorkflowRunId == workflowRunId);
                if (lastLog != null)
                {
                    workflow.LastLog = lastLog;
                }
            }
            return ServiceResult<List<WorkflowResponse>>.Success(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflows. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<List<WorkflowResponse>>.BadRequest("Failed to retrieve workflows: " + ex.Message);
        }
    }

    /// <summary>
    /// Retrieves a list of workflows based on agent name and workflow type.
    /// </summary>
    /// <param name="agentName">Optional agent name filter for workflows.</param>
    /// <param name="typeName">Optional workflow type filter for workflows.</param>
    /// <returns>A result containing the list of filtered workflows.</returns>
    public async Task<ServiceResult<List<WorkflowResponse>>> GetRunningWorkflowsByAgentAndType(string? agentName, string? typeName)
    {
        _logger.LogInformation("Retrieving workflows with filters - AgentName: {AgentName}, TypeName: {TypeName}",
            agentName ?? "null", typeName ?? "null");

        try
        {
            var client = _clientService.GetClient();
            var workflows = new List<WorkflowResponse>();
            var queryParts = new List<string>
            {
                // Add tenantId filter
                $"{Constants.TenantIdKey} = '{_tenantContext.TenantId}'",
                // status = running
                "ExecutionStatus = 'Running'"
            };

            // Add agent filter if specified
            if (!string.IsNullOrEmpty(agentName))
            {
                queryParts.Add($"{Constants.AgentKey} = '{agentName}'");
            }

            // Add workflow type filter if specified
            if (!string.IsNullOrEmpty(typeName))
            {
                queryParts.Add($"WorkflowType = '{typeName}'");
            }

            string listQuery = string.Join(" and ", queryParts);
            _logger.LogDebug("Executing workflow query: {Query}", listQuery);

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
            {
                var mappedWorkflow = MapWorkflowToResponse(workflow);
                workflows.Add(mappedWorkflow);
            }

            _logger.LogInformation("Retrieved {Count} workflows matching agent and type criteria", workflows.Count);
            return ServiceResult<List<WorkflowResponse>>.Success(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflows by agent and type. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<List<WorkflowResponse>>.BadRequest("Failed to retrieve workflows by agent and type: " + ex.Message);
        }
    }

    /// <summary>
    /// Builds a date range query string for filtering workflows.
    /// </summary>
    /// <param name="startTime">The start time of the range.</param>
    /// <param name="endTime">The end time of the range.</param>
    /// <param name="status">Optional status filter for workflows.</param>
    /// <param name="owner">Optional owner filter for workflows.</param>
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
    /// <param name="fetchHistory">Optional workflow history to analyze for current activity.</param>
    /// <param name="numberOfWorkers">The number of workers associated with the workflow.</param>
    /// <param name="taskQueue">The task queue associated with the workflow.</param>
    /// <returns>A WorkflowResponse containing the mapped data.</returns>
    private WorkflowResponse MapWorkflowToResponse(WorkflowExecution workflow, Temporalio.Common.WorkflowHistory? fetchHistory = null, string numberOfWorkers = "N/A", string taskQueue = "N/A")
    {
        var tenantId = ExtractMemoValue(workflow.Memo, Constants.TenantIdKey);
        var userId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);
        var agent = ExtractMemoValue(workflow.Memo, Constants.AgentKey) ?? workflow.WorkflowType;
        Temporalio.Api.History.V1.ActivityTaskScheduledEventAttributes? currentActivity = null;

        if (fetchHistory != null)
        {
            var eventsList = fetchHistory.Events.ToList(); // Convert to a list for indexing

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
            NumOfWorkers = numberOfWorkers,
            TaskQueue = taskQueue
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

    /// <summary>
    /// Retrieves the count of workers polling a task queue within the last minute.
    /// </summary>
    /// <param name="client">The Temporal client.</param>
    /// <param name="taskQueueName">The name of the task queue.</param>
    /// <returns>The count of recent workers as a string.</returns>
    private async Task<string> GetRecentWorkerCount(ITemporalClient client, string taskQueueName)
    {
        try
        {
            var describeQueueRequest = new DescribeTaskQueueRequest
            {
                Namespace = _tenantContext.GetTemporalConfig().FlowServerNamespace!,
                TaskQueue = new TaskQueue { Name = taskQueueName },
                ReportPollers = true,      // ask for the list of current pollers
                ReportStats = false,       // stats are optional here
                ReportTaskReachability = false
            };

            var describeQueueResponse = await client.WorkflowService.DescribeTaskQueueAsync(describeQueueRequest);
            
            var currentTime = DateTime.UtcNow;
            var oneMinuteAgo = currentTime.AddMinutes(-1);
            var recentWorkers = describeQueueResponse.Pollers
                .Where(poller => DateTimeOffset.FromUnixTimeSeconds(poller.LastAccessTime.Seconds).UtcDateTime >= oneMinuteAgo)
                .ToList();

            string recentWorkerCount = recentWorkers.Count.ToString();
            _logger.LogInformation(
                "TaskQueue {TaskQueue} has {WorkerCount} worker(s) polling it within the last minute",
                taskQueueName,
                recentWorkerCount
            );

            return recentWorkerCount;
        }
        catch (System.Exception)
        {
            _logger.LogWarning("Failed to retrieve recent worker count for TaskQueue {TaskQueue}", taskQueueName);
            return "N/A"; // Return "N/A" if unable to retrieve the count
        }

    }
}