using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Shared.Utils.Services;
using Features.WebApi.Models;
using Temporalio.Common;
using Shared.Utils.Temporal;
using Temporalio.Converters;
using Temporalio.Api.Enums.V1;
using Shared.Repositories;
using Shared.Services;

namespace Features.WebApi.Services;

public interface ITaskService
{
    Task<ServiceResult<TaskInfoResponse>> GetTaskById(string workflowId);
    Task<ServiceResult<object>> UpdateDraft(string workflowId, string updatedDraft);
    Task<ServiceResult<object>> CompleteTask(string workflowId);
    Task<ServiceResult<object>> RejectTask(string workflowId, string rejectionMessage);
    Task<ServiceResult<PaginatedTasksResponse>> GetTasks(int? pageSize, string? pageToken, string? agent, string? participantId, string? status);
}

/// <summary>
/// Service for managing human-in-the-loop task workflows.
/// Provides methods to query, update, complete, and reject tasks.
/// </summary>
public class TaskService : ITaskService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<TaskService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentRepository _agentRepository;
    private readonly IPermissionsService _permissionsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskService"/> class.
    /// </summary>
    /// <param name="clientFactory">The Temporal client factory for workflow operations.</param>
    /// <param name="logger">Logger for recording operational events.</param>
    /// <param name="tenantContext">Context containing tenant-specific information.</param>
    /// <param name="agentRepository">The agent repository for accessing agent information.</param>
    /// <param name="permissionsService">The permissions service for checking permissions.</param>
    public TaskService(
        ITemporalClientFactory clientFactory,
        ILogger<TaskService> logger,
        ITenantContext tenantContext,
        IAgentRepository agentRepository,
        IPermissionsService permissionsService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
    }

    /// <summary>
    /// Retrieves task information by workflow ID.
    /// </summary>
    /// <param name="workflowId">The workflow ID of the task.</param>
    /// <returns>A result containing the task information if found.</returns>
    public async Task<ServiceResult<TaskInfoResponse>> GetTaskById(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to retrieve task with empty workflowId");
            return ServiceResult<TaskInfoResponse>.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Retrieving task with workflow ID: {WorkflowId}", workflowId);
            var client = await _clientFactory.GetClientAsync();

            var workflowHandle = client.GetWorkflowHandle(workflowId);

            // Get workflow description for metadata
            var workflowDescription = await workflowHandle.DescribeAsync();

            // Query the task info from the workflow
            var taskInfo = await workflowHandle.QueryAsync<object>("GetTaskInfo", Array.Empty<object>());
            
            // Parse the task info from the query result
            var taskInfoJson = System.Text.Json.JsonSerializer.Serialize(taskInfo);
            var parsedTaskInfo = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                taskInfoJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Extract values from the parsed task info
            string? taskId = parsedTaskInfo.TryGetProperty("TaskId", out var taskIdProp) ? taskIdProp.GetString() : null;
            string? title = parsedTaskInfo.TryGetProperty("Title", out var titleProp) ? titleProp.GetString() : null;
            string? description = parsedTaskInfo.TryGetProperty("Description", out var descProp) ? descProp.GetString() : null;
            string? currentDraft = parsedTaskInfo.TryGetProperty("CurrentDraft", out var draftProp) ? draftProp.GetString() : null;
            string? participantId = parsedTaskInfo.TryGetProperty("ParticipantId", out var partProp) ? partProp.GetString() : null;
            bool isCompleted = parsedTaskInfo.TryGetProperty("IsCompleted", out var completedProp) ? completedProp.GetBoolean() : false;
            string? rejectionReason = parsedTaskInfo.TryGetProperty("RejectionReason", out var rejectProp) ? rejectProp.GetString() : null;

            // Extract metadata if present
            Dictionary<string, object>? metadata = null;
            if (parsedTaskInfo.TryGetProperty("Metadata", out var metadataProp) && metadataProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadataProp.GetRawText());
            }

            // Build the response with workflow metadata
            var response = new TaskInfoResponse
            {
                TaskId = taskId ?? "unknown",
                WorkflowId = workflowId,
                RunId = workflowDescription.RunId ?? "unknown",
                Title = title ?? "Untitled Task",
                Description = description ?? "",
                CurrentDraft = currentDraft,
                ParticipantId = participantId,
                Status = workflowDescription.Status.ToString(),
                IsCompleted = isCompleted,
                Error = rejectionReason,
                StartTime = workflowDescription.StartTime,
                CloseTime = workflowDescription.CloseTime,
                Metadata = metadata
            };

            _logger.LogInformation("Successfully retrieved task {WorkflowId}", workflowId);
            return ServiceResult<TaskInfoResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve task {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<TaskInfoResponse>.BadRequest($"Failed to retrieve task: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the draft work for a task.
    /// </summary>
    /// <param name="workflowId">The workflow ID of the task.</param>
    /// <param name="updatedDraft">The updated draft content.</param>
    /// <returns>A result indicating success or failure.</returns>
    public async Task<ServiceResult<object>> UpdateDraft(string workflowId, string updatedDraft)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to update draft with empty workflowId");
            return ServiceResult<object>.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Updating draft for task {WorkflowId}", workflowId);
            var client = await _clientFactory.GetClientAsync();

            var workflowHandle = client.GetWorkflowHandle(workflowId);

            // Send UpdateDraft signal
            await workflowHandle.SignalAsync("UpdateDraft", new object[] { updatedDraft });

            _logger.LogInformation("Successfully updated draft for task {WorkflowId}", workflowId);
            return ServiceResult<object>.Success(new { message = "Draft updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update draft for task {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<object>.BadRequest($"Failed to update draft: {ex.Message}");
        }
    }

    /// <summary>
    /// Marks a task as completed.
    /// </summary>
    /// <param name="workflowId">The workflow ID of the task.</param>
    /// <returns>A result indicating success or failure.</returns>
    public async Task<ServiceResult<object>> CompleteTask(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to complete task with empty workflowId");
            return ServiceResult<object>.BadRequest("WorkflowId cannot be empty");
        }

        try
        {
            _logger.LogInformation("Completing task {WorkflowId}", workflowId);
            var client = await _clientFactory.GetClientAsync();

            var workflowHandle = client.GetWorkflowHandle(workflowId);

            // Send CompleteTask signal
            await workflowHandle.SignalAsync("CompleteTask", Array.Empty<object>());

            _logger.LogInformation("Successfully completed task {WorkflowId}", workflowId);
            return ServiceResult<object>.Success(new { message = "Task completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete task {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<object>.BadRequest($"Failed to complete task: {ex.Message}");
        }
    }

    /// <summary>
    /// Rejects a task with a rejection message.
    /// </summary>
    /// <param name="workflowId">The workflow ID of the task.</param>
    /// <param name="rejectionMessage">The reason for rejection.</param>
    /// <returns>A result indicating success or failure.</returns>
    public async Task<ServiceResult<object>> RejectTask(string workflowId, string rejectionMessage)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to reject task with empty workflowId");
            return ServiceResult<object>.BadRequest("WorkflowId cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(rejectionMessage))
        {
            _logger.LogWarning("Attempt to reject task {WorkflowId} with empty rejection message", workflowId);
            return ServiceResult<object>.BadRequest("Rejection message cannot be empty");
        }

        try
        {
            _logger.LogInformation("Rejecting task {WorkflowId} with message: {RejectionMessage}", 
                workflowId, rejectionMessage);
            var client = await _clientFactory.GetClientAsync();

            var workflowHandle = client.GetWorkflowHandle(workflowId);

            // Send RejectTask signal
            await workflowHandle.SignalAsync("RejectTask", new object[] { rejectionMessage });

            _logger.LogInformation("Successfully rejected task {WorkflowId}", workflowId);
            return ServiceResult<object>.Success(new { message = "Task rejected successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reject task {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<object>.BadRequest($"Failed to reject task: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves a paginated list of tasks with optional filtering.
    /// </summary>
    /// <param name="pageSize">Number of items per page (default: 20, max: 100).</param>
    /// <param name="pageToken">Token for pagination continuation.</param>
    /// <param name="agent">Optional agent filter.</param>
    /// <param name="participantId">Optional participant/user ID filter.</param>
    /// <param name="status">Optional workflow execution status filter.</param>
    /// <returns>A result containing the paginated list of tasks.</returns>
    public async Task<ServiceResult<PaginatedTasksResponse>> GetTasks(
        int? pageSize, 
        string? pageToken, 
        string? agent, 
        string? participantId,
        string? status)
    {
        _logger.LogInformation(
            "Retrieving paginated tasks - PageSize: {PageSize}, PageToken: {PageToken}, Agent: {Agent}, ParticipantId: {ParticipantId}, Status: {Status}",
            pageSize ?? 20, pageToken ?? "null", agent ?? "null", participantId ?? "null", status ?? "null");

        // Validate page size
        var actualPageSize = pageSize ?? 20;
        if (actualPageSize <= 0 || actualPageSize > 100)
        {
            actualPageSize = 20;
        }

        try
        {
            var client = await _clientFactory.GetClientAsync();
            var tasks = new List<TaskInfoResponse>();

            // Build query with filters
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
                    return ServiceResult<PaginatedTasksResponse>.BadRequest("You do not have read permission to this agent");
                }
                queryParts.Add($"{Constants.AgentKey} = '{agent}'");
                queryParts.Add($"WorkflowType = '{agent}:Task Workflow'");
            }
            else
            {
                // If no specific agent, get all agents user has permission to
                var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
                if (agents == null || agents.Count == 0)
                {
                    return ServiceResult<PaginatedTasksResponse>.Success(new PaginatedTasksResponse
                    {
                        Tasks = new List<TaskInfoResponse>(),
                        NextPageToken = null,
                        PageSize = actualPageSize,
                        HasNextPage = false,
                        TotalCount = 0
                    });
                }
                var agentNames = agents.Select(a => a.Name).ToArray();
                queryParts.Add($"{Constants.AgentKey} in ({string.Join(",", agentNames.Select(a => "'" + a + "'"))})");
                
                // Add workflow type filter for all agents (Task Workflow)
                var workflowTypes = agentNames.Select(a => $"'{a}:Task Workflow'").ToArray();
                queryParts.Add($"WorkflowType in ({string.Join(",", workflowTypes)})");
            }

            // Add participantId filter if specified
            if (!string.IsNullOrEmpty(participantId))
            {
                queryParts.Add($"{Constants.UserIdKey} = '{participantId}'");
            }

            // Add status filter if specified
            if (!string.IsNullOrEmpty(status))
            {
                queryParts.Add($"ExecutionStatus = '{status}'");
            }

            var listQuery = string.Join(" and ", queryParts);
            _logger.LogDebug("Executing paginated tasks query: {Query}", listQuery);

            // Calculate pagination parameters
            int skipCount = 0;
            if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pageNumber))
            {
                skipCount = (pageNumber - 1) * actualPageSize;
            }

            var minRequiredItems = skipCount + actualPageSize + 1; // +1 to check for next page
            var fetchLimit = Math.Max(minRequiredItems, 100);

            var listOptions = new WorkflowListOptions
            {
                Limit = fetchLimit
            };

            var allTasks = new List<TaskInfoResponse>();
            var itemsProcessed = 0;

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery, listOptions))
            {
                var taskInfo = MapWorkflowToTaskInfo(workflow);
                allTasks.Add(taskInfo);
                itemsProcessed++;

                // If we have enough items for this page and to determine next page, break early
                if (itemsProcessed >= minRequiredItems)
                {
                    break;
                }
            }

            // Apply pagination to the collected results
            var totalResults = allTasks.Count;
            var startIndex = skipCount;
            var endIndex = Math.Min(startIndex + actualPageSize, totalResults);

            _logger.LogDebug(
                "Pagination details: TotalResults={TotalResults}, StartIndex={StartIndex}, EndIndex={EndIndex}, PageSize={PageSize}",
                totalResults, startIndex, endIndex, actualPageSize);

            // Get the tasks for this page
            tasks = allTasks.Skip(startIndex).Take(actualPageSize).ToList();

            // Determine if there's a next page
            string? nextPageToken = null;
            if (startIndex + actualPageSize < totalResults)
            {
                var nextPage = string.IsNullOrEmpty(pageToken) ? 2 : 
                    (int.TryParse(pageToken, out var currentPageNum) ? currentPageNum + 1 : 2);
                nextPageToken = nextPage.ToString();
            }
            else if (itemsProcessed >= fetchLimit && totalResults >= minRequiredItems - 1)
            {
                var nextPage = string.IsNullOrEmpty(pageToken) ? 2 : 
                    (int.TryParse(pageToken, out var currentPageNum) ? currentPageNum + 1 : 2);
                nextPageToken = nextPage.ToString();
            }

            var response = new PaginatedTasksResponse
            {
                Tasks = tasks,
                NextPageToken = nextPageToken,
                PageSize = actualPageSize,
                HasNextPage = nextPageToken != null,
                TotalCount = null // Temporal doesn't provide total count efficiently
            };

            _logger.LogInformation("Retrieved {Count} tasks for page", tasks.Count);
            return ServiceResult<PaginatedTasksResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated tasks. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<PaginatedTasksResponse>.InternalServerError("Failed to retrieve tasks");
        }
    }

    /// <summary>
    /// Maps a Temporal workflow execution to a task info response.
    /// </summary>
    /// <param name="workflow">The workflow execution to map.</param>
    /// <returns>A TaskInfoResponse containing the mapped data.</returns>
    private TaskInfoResponse MapWorkflowToTaskInfo(WorkflowExecution workflow)
    {
        var taskTitle = ExtractMemoValue(workflow.Memo, Constants.TaskTitleKey) ?? "Untitled Task";
        var taskDescription = ExtractMemoValue(workflow.Memo, Constants.TaskDescriptionKey) ?? "";
        var participantId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);

        // Extract taskId from workflow ID (format: tenantId:WorkflowType:taskId)
        var taskId = workflow.Id.Split(':').LastOrDefault() ?? workflow.Id;

        return new TaskInfoResponse
        {
            TaskId = taskId,
            WorkflowId = workflow.Id,
            RunId = workflow.RunId,
            Title = taskTitle,
            Description = taskDescription,
            ParticipantId = participantId,
            Status = workflow.Status.ToString(),
            IsCompleted = workflow.Status != Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Running,
            StartTime = workflow.StartTime,
            CloseTime = workflow.CloseTime
        };
    }

    /// <summary>
    /// Extracts a value from the workflow memo dictionary.
    /// </summary>
    /// <param name="memo">The memo dictionary containing workflow metadata.</param>
    /// <param name="key">The key to extract.</param>
    /// <returns>The extracted string value, or null if not found.</returns>
    private string? ExtractMemoValue(IReadOnlyDictionary<string, Temporalio.Converters.IEncodedRawValue> memo, string key)
    {
        if (memo.TryGetValue(key, out var memoValue))
        {
            return memoValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
        }
        return null;
    }
}

