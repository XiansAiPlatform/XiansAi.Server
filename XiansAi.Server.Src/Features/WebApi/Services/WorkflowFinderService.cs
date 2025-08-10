using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Temporalio.Converters;
using System.Text.Json;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Api.TaskQueue.V1;
using Shared.Utils.Temporal;
using Shared.Data;
using Shared.Data.Models;
using Features.WebApi.Repositories;
using Shared.Repositories;
using Shared.Utils.Services;
using Features.WebApi.Models;
using Temporalio.Common;
using Temporalio.Api.History.V1;
using Shared.Services;
using MongoDB.Bson;

namespace Features.WebApi.Services;

public interface IWorkflowFinderService
{
    Task<ServiceResult<WorkflowResponse>> GetWorkflow(string workflowId, string? runId = null);
    Task<ServiceResult<List<WorkflowsWithAgent>>> GetWorkflows(string? status);
    Task<ServiceResult<PaginatedWorkflowsResponse>> GetWorkflows(string? status, string? agent, int? pageSize, string? pageToken);
    Task<ServiceResult<List<WorkflowResponse>>> GetRunningWorkflowsByAgentAndType(string? agentName, string? typeName);
}

/// <summary>
/// Endpoint for retrieving and managing workflow information from Temporal.
/// </summary>
public class WorkflowFinderService : IWorkflowFinderService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowFinderService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IDatabaseService _databaseService;
    private readonly IAgentRepository _agentRepository;
    private readonly IPermissionsService _permissionsService;
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowFinderService"/> class.
    /// </summary>
    /// <param name="clientFactory">The Temporal client factory for workflow operations.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="tenantContext">Context containing tenant-specific information.</param>
    /// <param name="databaseService">The database service for accessing workflow logs.</param>
    /// <param name="agentRepository">The agent repository for accessing agent information.</param>
    /// <param name="permissionsService">The permissions service for checking permissions.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
    public WorkflowFinderService(
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowFinderService> logger,
        ITenantContext tenantContext,
        IDatabaseService databaseService,
        IAgentRepository agentRepository,
        IPermissionsService permissionsService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _databaseService = databaseService;
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
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
            var client = await _clientFactory.GetClientAsync();
            var workflowHandle = client.GetWorkflowHandle(workflowId, workflowRunId);

            var workflowDescription = await workflowHandle.DescribeAsync();

            var agent = ExtractMemoValue(workflowDescription.Memo, Constants.AgentKey) ?? throw new Exception("Agent not found");
            var hasReadPermission = await _permissionsService.HasReadPermission(agent);
            if (!hasReadPermission.Data)
            {
                return ServiceResult<WorkflowResponse>.BadRequest("You do not have read permission to this agent");
            }

            //log the workflow description object
            _logger.LogDebug("Workflow description: {Description}", JsonSerializer.Serialize(workflowDescription));
            string recentWorkerCount = await GetRecentWorkerCount(client, workflowDescription.TaskQueue!);

            var history = await workflowHandle.FetchHistoryAsync();
            var workflow = MapWorkflowToResponse(workflowDescription, history, recentWorkerCount, workflowDescription.TaskQueue!);

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
    /// Retrieves a list of workflows based on specified filters with pagination support.
    /// </summary>
    /// <param name="status">Optional status filter for workflows.</param>
    /// <param name="agent">Optional agent filter for workflows.</param>
    /// <param name="pageSize">Number of items per page (default: 20, max: 100).</param>
    /// <param name="pageToken">Token for pagination continuation.</param>
    /// <returns>A result containing the paginated list of workflows.</returns>
    public async Task<ServiceResult<PaginatedWorkflowsResponse>> GetWorkflows(string? status, string? agent, int? pageSize, string? pageToken)
    {
        _logger.LogInformation("Retrieving paginated workflows with filters - Status: {Status}, Agent: {Agent}, PageSize: {PageSize}, PageToken: {PageToken}", 
            status ?? "null", agent ?? "null", pageSize ?? 20, pageToken ?? "null");

        // Validate page size
        var actualPageSize = pageSize ?? 20;
        if (actualPageSize <= 0 || actualPageSize > 100)
        {
            actualPageSize = 20;
        }

        try
        {
            var client = await _clientFactory.GetClientAsync();
            var workflows = new List<WorkflowResponse>();

            // Build query for agent filtering
            var queryParts = new List<string>
            {
                $"{Constants.TenantIdKey} = '{_tenantContext.TenantId}'"
            };

            // Add agent filter if specified
            if (!string.IsNullOrEmpty(agent))
            {
                // Check if user has permission to read this agent
                var hasReadPermission = await _permissionsService.HasReadPermission(agent);
                if (!hasReadPermission.Data)
                {
                    return ServiceResult<PaginatedWorkflowsResponse>.BadRequest("You do not have read permission to this agent");
                }
                queryParts.Add($"{Constants.AgentKey} = '{agent}'");
            }
            else
            {
                // If no specific agent, get all agents user has permission to
                var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
                if (agents == null || agents.Count == 0)
                {
                    return ServiceResult<PaginatedWorkflowsResponse>.Success(new PaginatedWorkflowsResponse
                    {
                        Workflows = new List<WorkflowResponse>(),
                        NextPageToken = null,
                        PageSize = actualPageSize,
                        HasNextPage = false,
                        TotalCount = 0
                    });
                }
                var agentNames = agents.Select(a => a.Name).ToArray();
                queryParts.Add($"{Constants.AgentKey} in ({string.Join(",", agentNames.Select(a => "'" + a + "'"))})");
            }

            // Add status filter if specified
            if (!string.IsNullOrEmpty(status))
            {
                status = status.ToLower();
                string normalizedStatus = status switch
                {
                    "running" => "Running",
                    "completed" => "Completed",
                    "failed" => "Failed",
                    "canceled" => "Canceled",
                    "terminated" => "Terminated",
                    "continuedasnew" => "ContinuedAsNew",
                    "timedout" => "TimedOut",
                    _ => status
                };
                queryParts.Add($"ExecutionStatus = '{normalizedStatus}'");
            }

            var listQuery = string.Join(" and ", queryParts);
            _logger.LogDebug("Executing paginated workflow query: {Query}", listQuery);

            // Calculate pagination parameters
            int skipCount = 0;
            if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pageNumber))
            {
                skipCount = (pageNumber - 1) * actualPageSize;
            }

            // To avoid fetching too many items for later pages, we'll use a reasonable limit
            // but ensure we get enough for the requested page
            var minRequiredItems = skipCount + actualPageSize + 1; // +1 to check for next page
            var fetchLimit = Math.Max(minRequiredItems, 100); // Fetch at least 100 or the required amount
            
            var listOptions = new WorkflowListOptions
            {
                Limit = fetchLimit
            };

            var allWorkflows = new List<WorkflowResponse>();
            var itemsProcessed = 0;
            
            await foreach (var workflow in client.ListWorkflowsAsync(listQuery, listOptions))
            {
                var mappedWorkflow = MapWorkflowToResponse(workflow);
                allWorkflows.Add(mappedWorkflow);
                itemsProcessed++;
                
                // If we have enough items for this page and to determine next page, we can break early
                if (itemsProcessed >= minRequiredItems)
                {
                    break;
                }
            }

            // Apply pagination to the collected results
            var totalResults = allWorkflows.Count;
            var startIndex = skipCount;
            var endIndex = Math.Min(startIndex + actualPageSize, totalResults);
            
            _logger.LogDebug("Pagination details: TotalResults={TotalResults}, StartIndex={StartIndex}, EndIndex={EndIndex}, PageSize={PageSize}, PageToken={PageToken}, SkipCount={SkipCount}, FetchLimit={FetchLimit}, ItemsProcessed={ItemsProcessed}", 
                totalResults, startIndex, endIndex, actualPageSize, pageToken ?? "null", skipCount, fetchLimit, itemsProcessed);
            
            // Get the workflows for this page
            workflows = allWorkflows.Skip(startIndex).Take(actualPageSize).ToList();
            
            // Determine if there's a next page
            string? nextPageToken = null;
            if (startIndex + actualPageSize < totalResults)
            {
                // We have more results available
                var nextPage = string.IsNullOrEmpty(pageToken) ? 2 : (int.TryParse(pageToken, out var currentPageNum) ? currentPageNum + 1 : 2);
                nextPageToken = nextPage.ToString();
            }
            else if (itemsProcessed >= fetchLimit && totalResults >= minRequiredItems - 1)
            {
                // We may have more results but hit our fetch limit
                var nextPage = string.IsNullOrEmpty(pageToken) ? 2 : (int.TryParse(pageToken, out var currentPageNum) ? currentPageNum + 1 : 2);
                nextPageToken = nextPage.ToString();
            }
            
            _logger.LogInformation("Pagination result: Retrieved {ActualCount} workflows out of {TotalResults} for page {CurrentPage}, HasNextPage={HasNextPage}", 
                workflows.Count, totalResults, pageToken ?? "1", nextPageToken != null);

            // Retrieve last logs for workflows in this page
            var logRepository = new LogRepository(_databaseService);
            var logs = await logRepository.GetLastLogAsync(null, null);
            foreach (var workflow in workflows)
            {
                var lastLog = logs.FirstOrDefault(x => x.WorkflowRunId == workflow.RunId);
                if (lastLog != null)
                {
                    workflow.LastLog = lastLog;
                }
            }

            var response = new PaginatedWorkflowsResponse
            {
                Workflows = workflows,
                NextPageToken = nextPageToken,
                PageSize = actualPageSize,
                HasNextPage = nextPageToken != null,
                TotalCount = null // Temporal doesn't provide total count efficiently
            };

            _logger.LogInformation("Retrieved {Count} workflows for page", workflows.Count);
            return ServiceResult<PaginatedWorkflowsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated workflows. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<PaginatedWorkflowsResponse>.InternalServerError("Failed to retrieve workflows: " + ex.Message);
        }
    }

    /// <summary>
    /// Retrieves a list of workflows based on specified filters (legacy method - maintains backward compatibility).
    /// </summary>
    /// <param name="status">Optional status filter for workflows.</param>
    /// <returns>A result containing the list of filtered workflows.</returns>
    public async Task<ServiceResult<List<WorkflowsWithAgent>>> GetWorkflows(string? status)
    {
        _logger.LogInformation("Retrieving workflows with filters - Status: {Status}", status ?? "null");

        var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);

        if (agents == null || agents.Count == 0)
        {
            return ServiceResult<List<WorkflowsWithAgent>>.Success(new List<WorkflowsWithAgent>());
        }
        var agentNames = agents.Select(a => a.Name).ToArray();
        var allWorkflowResponses = new List<WorkflowResponse>();

        try
        {
            var client = await _clientFactory.GetClientAsync();

            var listQuery = BuildQuery(agentNames, status);

            _logger.LogDebug("Executing workflow query: {Query}", string.IsNullOrEmpty(listQuery) ? "No date filters" : listQuery);

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery))
            {
                var mappedWorkflow = MapWorkflowToResponse(workflow);
                allWorkflowResponses.Add(mappedWorkflow);
            }
            _logger.LogInformation("Retrieved {Count} workflows matching the specified criteria", allWorkflowResponses.Count);

            // retrieve last logs for each workflow run
            var logRepository = new LogRepository(_databaseService);
            var logs = await logRepository.GetLastLogAsync(null, null);
            foreach (var workflow in allWorkflowResponses)
            {
                var workflowId = workflow.WorkflowId;
                var workflowRunId = workflow.RunId;

                var lastLog = logs.FirstOrDefault(x => x.WorkflowRunId == workflowRunId);
                if (lastLog != null)
                {
                    workflow.LastLog = lastLog;
                }
            }

            // Group workflows by agent
            var workflowsGroupedByAgent = allWorkflowResponses
                .GroupBy(w => w.Agent)
                .Select(group => 
                {
                    var dbAgent = agents.FirstOrDefault(a => a.Name == group.Key);
                    if (dbAgent == null)
                    {
                        // Create a minimal agent object with just the name when no database record exists
                        dbAgent = new Agent
                        {
                            Id = "-deleted-" + Guid.NewGuid().ToString(),
                            Name = group.Key,
                            Tenant = _tenantContext.TenantId,
                            CreatedBy = "unknown-agent", // Default value for unknown agents
                            CreatedAt = DateTime.MinValue,
                            OwnerAccess = new List<string>(),
                            ReadAccess = new List<string>(),
                            WriteAccess = new List<string>()
                        };
                    }
                    
                    return new WorkflowsWithAgent
                    {
                        Agent = dbAgent,
                        Workflows = group.ToList()
                    };
                })
                .ToList();

            return ServiceResult<List<WorkflowsWithAgent>>.Success(workflowsGroupedByAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workflows. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<List<WorkflowsWithAgent>>.InternalServerError("Failed to retrieve workflows: " + ex.Message);
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
            var client = await _clientFactory.GetClientAsync();
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
    /// <param name="agents">The agent name to filter workflows by.</param>
    /// <param name="status">Optional status filter for workflows.</param>
    /// <returns>A query string for temporal workflow filtering.</returns>
    private string BuildQuery(string[] agents, string? status)
    {
        var queryParts = new List<string>
        {
            // Add tenantId filter
            $"{Constants.TenantIdKey} = '{_tenantContext.TenantId}'"
        };    


        if (agents.Length > 0) 
        {
            queryParts.Add($"{Constants.AgentKey} in ({string.Join(",", agents.Select(a => "'" + a + "'"))})");
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
        var andParts = string.Join(" and ", queryParts);

        _logger.LogDebug("Built query: {Query}", andParts);

        return andParts;
    }

    /// <summary>
    /// Identifies the current activity from the workflow history.
    /// </summary>
    /// <param name="workflowHistory">The workflow history to analyze.</param>
    /// <returns>The current activity if found, otherwise null.</returns>
    private ActivityTaskScheduledEventAttributes? IdentifyCurrentActivity(List<HistoryEvent> workflowHistory)
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
    /// <param name="history">Optional workflow history to analyze for current activity.</param>
    /// <param name="numberOfWorkers">The number of workers associated with the workflow.</param>
    /// <param name="taskQueue">The task queue associated with the workflow.</param>
    /// <returns>A WorkflowResponse containing the mapped data.</returns>
    private WorkflowResponse MapWorkflowToResponse(WorkflowExecution workflow, WorkflowHistory? history = null, string numberOfWorkers = "N/A", string taskQueue = "N/A")
    {
        var tenantId = ExtractMemoValue(workflow.Memo, Constants.TenantIdKey);
        var userId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);
        var agent = ExtractMemoValue(workflow.Memo, Constants.AgentKey);

        ActivityTaskScheduledEventAttributes? currentActivity = null;

        if (history != null)
        {
            var eventsList = history.Events.ToList(); // Convert to a list for indexing

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
            Agent = agent ?? throw new Exception("Agent not found"),
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