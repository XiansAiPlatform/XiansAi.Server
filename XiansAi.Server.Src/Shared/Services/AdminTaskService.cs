using Shared.Utils;
using Temporalio.Client;
using Shared.Utils.Services;
using Shared.Utils.Temporal;

namespace Shared.Services;

/// <summary>
/// Response model for task information in admin context.
/// </summary>
public class AdminTaskInfoResponse
{
    public required string WorkflowId { get; set; }
    public required string RunId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string WorkflowStatus { get; set; }
    public bool TimedOut { get; set; }
    public string? InitialWork { get; set; }
    public string? FinalWork { get; set; }
    public string? ParticipantId { get; set; }
    public string? Status { get; set; }
    public bool IsCompleted { get; set; }
    public string[]? AvailableActions { get; set; }
    public string? PerformedAction { get; set; }
    public string? Comment { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CloseTime { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    
    // Admin-specific fields
    public string? AgentName { get; set; }
    public string? ActivationName { get; set; }
    public string? TenantId { get; set; }
}

/// <summary>
/// Response model for paginated admin tasks list.
/// </summary>
public class AdminPaginatedTasksResponse
{
    public required List<AdminTaskInfoResponse> Tasks { get; set; }
    public string? NextPageToken { get; set; }
    public required int PageSize { get; set; }
    public required bool HasNextPage { get; set; }
    public int? TotalCount { get; set; }
}

public interface IAdminTaskService
{
    Task<ServiceResult<AdminTaskInfoResponse>> GetTaskById(string workflowId);
    Task<ServiceResult<AdminPaginatedTasksResponse>> GetTasks(
        string tenantId,
        int? pageSize,
        string? pageToken,
        string? agentName,
        string? activationName,
        string? participantId,
        string? status);
    Task<ServiceResult<object>> UpdateDraft(string workflowId, string updatedDraft);
    Task<ServiceResult<object>> PerformAction(string workflowId, string action, string? comment);
}

/// <summary>
/// Service for managing tasks in admin context.
/// Provides methods to query tasks across tenants with various filters.
/// </summary>
public class AdminTaskService : IAdminTaskService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<AdminTaskService> _logger;

    public AdminTaskService(
        ITemporalClientFactory clientFactory,
        ILogger<AdminTaskService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves task information by workflow ID.
    /// </summary>
    public async Task<ServiceResult<AdminTaskInfoResponse>> GetTaskById(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to retrieve task with empty workflowId");
            return ServiceResult<AdminTaskInfoResponse>.BadRequest("WorkflowId cannot be empty");
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
            string? title = parsedTaskInfo.TryGetProperty("Title", out var titleProp) ? titleProp.GetString() : null;
            string? description = parsedTaskInfo.TryGetProperty("Description", out var descProp) ? descProp.GetString() : null;
            string? initialWork = parsedTaskInfo.TryGetProperty("InitialWork", out var initialWorkProp) ? initialWorkProp.GetString() : null;
            string? finalWork = parsedTaskInfo.TryGetProperty("FinalWork", out var finalWorkProp) ? finalWorkProp.GetString() : null;
            string? participantId = parsedTaskInfo.TryGetProperty("ParticipantId", out var partProp) ? partProp.GetString() : null;
            bool isCompleted = parsedTaskInfo.TryGetProperty("IsCompleted", out var completedProp) && completedProp.GetBoolean();
            bool timedOut = parsedTaskInfo.TryGetProperty("TimedOut", out var timedOutProp) && timedOutProp.GetBoolean();
            
            // Extract action-based fields
            string? performedAction = parsedTaskInfo.TryGetProperty("PerformedAction", out var actionProp) ? actionProp.GetString() : null;
            string? comment = parsedTaskInfo.TryGetProperty("Comment", out var commentProp) ? commentProp.GetString() : null;
            
            // Extract available actions
            string[]? availableActions = null;
            if (parsedTaskInfo.TryGetProperty("AvailableActions", out var actionsProp) && actionsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                availableActions = actionsProp.EnumerateArray()
                    .Where(a => a.ValueKind == System.Text.Json.JsonValueKind.String)
                    .Select(a => a.GetString()!)
                    .ToArray();
            }

            // Extract metadata if present
            Dictionary<string, object>? metadata = null;
            if (parsedTaskInfo.TryGetProperty("Metadata", out var metadataProp) && metadataProp.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(metadataProp.GetRawText());
            }

            // Extract admin-specific fields from workflow memo
            var agentName = ExtractMemoValue(workflowDescription.Memo, Constants.AgentKey);
            var activationName = ExtractMemoValue(workflowDescription.Memo, Constants.IdPostfixKey);
            var tenantId = ExtractMemoValue(workflowDescription.Memo, Constants.TenantIdKey);

            // Build the response with workflow metadata
            var response = new AdminTaskInfoResponse
            {
                WorkflowId = workflowId,
                RunId = workflowDescription.RunId ?? "unknown",
                Title = title ?? "Untitled Task",
                Description = description ?? "",
                InitialWork = initialWork,
                FinalWork = finalWork,
                TimedOut = timedOut,
                ParticipantId = participantId,
                WorkflowStatus = workflowDescription.Status.ToString(),
                Status = workflowDescription.Status.ToString(),
                IsCompleted = isCompleted,
                AvailableActions = availableActions,
                PerformedAction = performedAction,
                Comment = comment,
                StartTime = workflowDescription.StartTime,
                CloseTime = workflowDescription.CloseTime,
                Metadata = metadata,
                AgentName = agentName,
                ActivationName = activationName,
                TenantId = tenantId
            };

            _logger.LogInformation("Successfully retrieved task {WorkflowId}", workflowId);
            return ServiceResult<AdminTaskInfoResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve task {WorkflowId}. Error: {ErrorMessage}",
                workflowId, ex.Message);
            return ServiceResult<AdminTaskInfoResponse>.BadRequest($"Failed to retrieve task: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves a paginated list of tasks with optional filtering.
    /// </summary>
    public async Task<ServiceResult<AdminPaginatedTasksResponse>> GetTasks(
        string tenantId,
        int? pageSize,
        string? pageToken,
        string? agentName,
        string? activationName,
        string? participantId,
        string? status)
    {
        _logger.LogInformation(
            "Retrieving paginated tasks - TenantId: {TenantId}, PageSize: {PageSize}, PageToken: {PageToken}, AgentName: {AgentName}, ActivationName: {ActivationName}, ParticipantId: {ParticipantId}, Status: {Status}",
            tenantId, pageSize ?? 20, pageToken ?? "null", agentName ?? "null", activationName ?? "null", participantId ?? "null", status ?? "null");

        // Validate page size
        var actualPageSize = pageSize ?? 20;
        if (actualPageSize <= 0 || actualPageSize > 100)
        {
            actualPageSize = 20;
        }

        try
        {
            var client = await _clientFactory.GetClientAsync();
            var tasks = new List<AdminTaskInfoResponse>();

            // Build query with filters
            var queryParts = new List<string>
            {
                $"{Constants.TenantIdKey} = '{tenantId}'"
            };

            // Add agentName filter if specified
            if (!string.IsNullOrEmpty(agentName))
            {
                queryParts.Add($"{Constants.AgentKey} = '{agentName}'");
                queryParts.Add($"WorkflowType = '{agentName}:Task Workflow'");
            }
            // Note: When no agent is specified, we need to filter out non-Task workflows
            // after fetching, as Temporal doesn't support wildcard matching in WorkflowType

            // Add activationName filter if specified (maps to idPostfix)
            if (!string.IsNullOrEmpty(activationName))
            {
                queryParts.Add($"{Constants.IdPostfixKey} = '{activationName}'");
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

            var allTasks = new List<AdminTaskInfoResponse>();
            var itemsProcessed = 0;

            await foreach (var workflow in client.ListWorkflowsAsync(listQuery, listOptions))
            {
                // Filter to only include Task Workflows when agentName is not specified
                if (string.IsNullOrEmpty(agentName) && !workflow.Id.Contains(":Task Workflow:"))
                {
                    continue; // Skip non-Task workflows
                }

                var taskInfo = MapWorkflowToTaskInfo(workflow);
                allTasks.Add(taskInfo);
                itemsProcessed++;

                // If we have enough items for this page and to determine next page, break early
                if (itemsProcessed >= minRequiredItems)
                {
                    break;
                }
            }

            // Order by StartTime descending to get latest tasks first
            allTasks = allTasks.OrderByDescending(t => t.StartTime).ToList();

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

            var response = new AdminPaginatedTasksResponse
            {
                Tasks = tasks,
                NextPageToken = nextPageToken,
                PageSize = actualPageSize,
                HasNextPage = nextPageToken != null,
                TotalCount = null // Temporal doesn't provide total count efficiently
            };

            _logger.LogInformation("Retrieved {Count} tasks for page", tasks.Count);
            return ServiceResult<AdminPaginatedTasksResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated tasks. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminPaginatedTasksResponse>.InternalServerError("Failed to retrieve tasks");
        }
    }

    /// <summary>
    /// Maps a Temporal workflow execution to a task info response.
    /// Extracts available actions and admin-specific fields from the workflow memo.
    /// </summary>
    private AdminTaskInfoResponse MapWorkflowToTaskInfo(WorkflowExecution workflow)
    {
        var taskTitle = ExtractMemoValue(workflow.Memo, Constants.TaskTitleKey) ?? "Untitled Task";
        var taskDescription = ExtractMemoValue(workflow.Memo, Constants.TaskDescriptionKey) ?? "";
        var participantId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);
        var taskActionsStr = ExtractMemoValue(workflow.Memo, Constants.TaskActionsKey);
        var agentName = ExtractMemoValue(workflow.Memo, Constants.AgentKey);
        var activationName = ExtractMemoValue(workflow.Memo, Constants.IdPostfixKey);
        var tenantId = ExtractMemoValue(workflow.Memo, Constants.TenantIdKey);

        // Parse available actions from comma-separated string
        string[]? availableActions = null;
        if (!string.IsNullOrEmpty(taskActionsStr))
        {
            availableActions = taskActionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return new AdminTaskInfoResponse
        {
            WorkflowId = workflow.Id,
            RunId = workflow.RunId,
            WorkflowStatus = workflow.Status.ToString(),
            Title = taskTitle,
            Description = taskDescription,
            ParticipantId = participantId,
            AvailableActions = availableActions,
            StartTime = workflow.StartTime,
            CloseTime = workflow.CloseTime,
            AgentName = agentName,
            ActivationName = activationName,
            TenantId = tenantId
        };
    }

    /// <summary>
    /// Updates the draft work for a task.
    /// </summary>
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
    /// Performs an action on a task with an optional comment.
    /// </summary>
    public async Task<ServiceResult<object>> PerformAction(string workflowId, string action, string? comment)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            _logger.LogWarning("Attempt to perform action with empty workflowId");
            return ServiceResult<object>.BadRequest("WorkflowId cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            _logger.LogWarning("Attempt to perform action with empty action for task {WorkflowId}", workflowId);
            return ServiceResult<object>.BadRequest("Action cannot be empty");
        }

        try
        {
            _logger.LogInformation("Performing action '{Action}' on task {WorkflowId} with comment: {Comment}", 
                action, workflowId, comment ?? "(none)");
            var client = await _clientFactory.GetClientAsync();

            var workflowHandle = client.GetWorkflowHandle(workflowId);

            // Send PerformAction signal with TaskActionRequest payload
            var actionRequest = new { Action = action, Comment = comment };
            await workflowHandle.SignalAsync("PerformAction", new object[] { actionRequest });

            _logger.LogInformation("Successfully performed action '{Action}' on task {WorkflowId}", action, workflowId);
            return ServiceResult<object>.Success(new { message = $"Action '{action}' performed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform action '{Action}' on task {WorkflowId}. Error: {ErrorMessage}",
                action, workflowId, ex.Message);
            return ServiceResult<object>.BadRequest($"Failed to perform action: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a value from the workflow memo dictionary.
    /// </summary>
    private string? ExtractMemoValue(IReadOnlyDictionary<string, Temporalio.Converters.IEncodedRawValue> memo, string key)
    {
        if (memo.TryGetValue(key, out var memoValue))
        {
            return memoValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
        }
        return null;
    }
}
