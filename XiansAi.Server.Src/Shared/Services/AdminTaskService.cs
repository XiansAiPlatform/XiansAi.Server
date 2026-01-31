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
    public required string Status { get; set; }
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

/// <summary>
/// Response model for task statistics.
/// </summary>
public class TaskStatisticsResponse
{
    public required int Pending { get; set; }
    public required int Completed { get; set; }
    public required int TimedOut { get; set; }
    public required int Cancelled { get; set; }
    public required int Total { get; set; }
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
    Task<ServiceResult<TaskStatisticsResponse>> GetTaskStatistics(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate,
        string? participantId);
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
                RunId = workflowDescription.RunId,
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
    /// Uses CountWorkflow API for efficient total count and improved pagination logic.
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
            
            // Build base query with filters
            var queryParts = new List<string>
            {
                $"{Constants.TenantIdKey} = '{tenantId}'",
                "TaskQueue STARTS_WITH 'hitl_task:'"
            };

            // Add agentName filter if specified
            if (!string.IsNullOrEmpty(agentName))
            {
                queryParts.Add($"{Constants.AgentKey} = '{agentName}'");
            }

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

            // Get total count efficiently using CountWorkflow API if available
            int? totalCount = await GetWorkflowCountAsync(client, listQuery, agentName);

            // Calculate pagination parameters
            int skipCount = 0;
            if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pageNumber))
            {
                skipCount = (pageNumber - 1) * actualPageSize;
            }

            // Fetch exactly what we need (no extra item needed since we have total count)
            var listOptions = new WorkflowListOptions
            {
                Limit = actualPageSize // No +1 needed - we use totalCount for next page detection
            };

            var allTasks = new List<AdminTaskInfoResponse>();
            var itemsProcessed = 0;
            var tasksSkipped = 0;



            await foreach (var workflow in client.ListWorkflowsAsync(listQuery, listOptions))
            {
                // Skip items for pagination
                if (tasksSkipped < skipCount)
                {
                    tasksSkipped++;
                    continue;
                }

                var taskInfo = MapWorkflowToTaskInfo(workflow);
                allTasks.Add(taskInfo);
                itemsProcessed++;

                // Stop when we have exactly what we need
                if (itemsProcessed >= actualPageSize)
                {
                    break;
                }
            }

            // Order by StartTime descending to get latest tasks first
            allTasks = allTasks.OrderByDescending(t => t.StartTime).ToList();

            // Calculate current page and determine if there's a next page using total count
            var currentPageNum = string.IsNullOrEmpty(pageToken) ? 1 : 
                (int.TryParse(pageToken, out var pageNum) ? pageNum : 1);

            bool hasNextPage;
            if (totalCount.HasValue)
            {
                // Use total count to determine next page - much more efficient!
                hasNextPage = (currentPageNum * actualPageSize) < totalCount.Value;
            }
            else
            {
                // Fallback: if no total count, assume there's a next page if we got a full page
                hasNextPage = allTasks.Count == actualPageSize;
            }

            string? nextPageToken = null;
            if (hasNextPage)
            {
                nextPageToken = (currentPageNum + 1).ToString();
            }

            var response = new AdminPaginatedTasksResponse
            {
                Tasks = allTasks,
                NextPageToken = nextPageToken,
                PageSize = actualPageSize,
                HasNextPage = hasNextPage,
                TotalCount = totalCount // Now efficiently retrieved
            };

            _logger.LogInformation("Retrieved {Count} tasks for page (Total: {TotalCount})", 
                allTasks.Count, totalCount?.ToString() ?? "unknown");
            return ServiceResult<AdminPaginatedTasksResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve paginated tasks. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminPaginatedTasksResponse>.InternalServerError("Failed to retrieve tasks");
        }
    }

    /// <summary>
    /// Efficiently gets the total count of workflows matching the query using CountWorkflow API.
    /// Falls back to null if the API is not available or fails.
    /// </summary>
    private async Task<int?> GetWorkflowCountAsync(ITemporalClient client, string listQuery, string? agentName)
    {
        try
        {
            // Build count query - add Task Workflow filter when agentName is not specified
            var countQuery = listQuery;

            _logger.LogDebug("Executing count query: {CountQuery}", countQuery);

            // Call CountWorkflowsAsync directly - available in Temporal Server v1.20+
            var countResponse = await client.CountWorkflowsAsync(countQuery);
            _logger.LogDebug("CountWorkflow API returned: {Count}", countResponse.Count);
            return (int)countResponse.Count;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogDebug("CountWorkflow API not supported in this Temporal version: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get workflow count using CountWorkflow API, continuing without total count");
            return null;
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
            Status = workflow.Status.ToString(),
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
    /// Retrieves task statistics for a tenant within a date range.
    /// Uses CountWorkflow API for efficient counting when available.
    /// </summary>
    public async Task<ServiceResult<TaskStatisticsResponse>> GetTaskStatistics(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate,
        string? participantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Attempt to retrieve task statistics with empty tenantId");
            return ServiceResult<TaskStatisticsResponse>.BadRequest("TenantId cannot be empty");
        }

        if (!startDate.HasValue || !endDate.HasValue)
        {
            _logger.LogWarning("Attempt to retrieve task statistics without date range");
            return ServiceResult<TaskStatisticsResponse>.BadRequest("StartDate and EndDate are required");
        }

        if (startDate.Value > endDate.Value)
        {
            _logger.LogWarning("Invalid date range: startDate {StartDate} is after endDate {EndDate}", 
                startDate.Value, endDate.Value);
            return ServiceResult<TaskStatisticsResponse>.BadRequest("StartDate cannot be after EndDate");
        }

        try
        {
            _logger.LogInformation(
                "Retrieving task statistics - TenantId: {TenantId}, StartDate: {StartDate}, EndDate: {EndDate}, ParticipantId: {ParticipantId}",
                tenantId, startDate.Value, endDate.Value, participantId ?? "null");

            var client = await _clientFactory.GetClientAsync();

            // Build base query with common filters
            var baseQueryParts = new List<string>
            {
                $"{Constants.TenantIdKey} = '{tenantId}'",
                "WorkflowType STARTS_WITH 'Task Workflow'", // Filter for Task Workflows only
                $"StartTime >= '{startDate.Value:yyyy-MM-ddTHH:mm:ssZ}'",
                $"StartTime <= '{endDate.Value:yyyy-MM-ddTHH:mm:ssZ}'"
            };

            // Add participantId filter if specified
            if (!string.IsNullOrEmpty(participantId))
            {
                baseQueryParts.Add($"{Constants.UserIdKey} = '{participantId}'");
            }

            var baseQuery = string.Join(" and ", baseQueryParts);
            _logger.LogDebug("Base statistics query: {Query}", baseQuery);

            // Try to use CountWorkflow API for efficient statistics
            var statistics = await GetTaskStatisticsUsingCountApi(client, baseQuery);
            if (statistics != null)
            {
                return ServiceResult<TaskStatisticsResponse>.Success(statistics);
            }

            // Fallback to the original method if CountWorkflow API is not available
            _logger.LogDebug("CountWorkflow API not available, falling back to listing workflows");
            return await GetTaskStatisticsUsingList(client, baseQuery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve task statistics. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<TaskStatisticsResponse>.InternalServerError("Failed to retrieve task statistics");
        }
    }

    /// <summary>
    /// Attempts to get task statistics using the efficient CountWorkflow API with GROUP BY.
    /// Returns null if the API is not available or fails.
    /// </summary>
    private async Task<TaskStatisticsResponse?> GetTaskStatisticsUsingCountApi(ITemporalClient client, string baseQuery)
    {
        try
        {
            _logger.LogDebug("Using CountWorkflow API with GROUP BY for statistics");

            // Use GROUP BY to get all status counts in a single query - much more efficient!
            var groupedQuery = $"{baseQuery} GROUP BY ExecutionStatus";
            
            var countResponse = await client.CountWorkflowsAsync(groupedQuery);
            
            var totalCount = (int)countResponse.Count;

            // Parse groups to extract counts by status
            int pending = 0;
            int completed = 0;
            int cancelled = 0;

            foreach (var group in countResponse.Groups)
            {
                if (group.GroupValues.Count > 0)
                {
                    var statusValue = group.GroupValues.FirstOrDefault();
                    var groupCount = (int)group.Count;

                    switch (statusValue)
                    {
                        case "Running":
                            pending = groupCount;
                            break;
                        case "Completed":
                            completed = groupCount;
                            break;
                        case "Canceled":
                        case "Terminated":
                        case "Failed":
                            cancelled += groupCount;
                            break;
                    }
                }
            }

            // For timed out tasks, we still need to query individual workflows since it's stored in memo
            var timedOut = await GetTimedOutCountUsingList(client, baseQuery);

            var response = new TaskStatisticsResponse
            {
                Pending = pending,
                Completed = completed,
                TimedOut = timedOut,
                Cancelled = cancelled,
                Total = totalCount
            };

            _logger.LogInformation(
                "Task statistics retrieved using CountWorkflow API with GROUP BY - Total: {Total}, Pending: {Pending}, Completed: {Completed}, TimedOut: {TimedOut}, Cancelled: {Cancelled}",
                totalCount, pending, completed, timedOut, cancelled);

            return response;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogDebug("CountWorkflow API not supported in this Temporal version: {Message}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get statistics using CountWorkflow API, will fallback to list method");
            return null;
        }
    }

    /// <summary>
    /// Gets timed out task count by examining workflow memos.
    /// This requires listing workflows since timedOut is stored in memo, not as execution status.
    /// </summary>
    private async Task<int> GetTimedOutCountUsingList(ITemporalClient client, string baseQuery)
    {
        try
        {
            var listOptions = new WorkflowListOptions { Limit = 1000 }; // Reasonable limit for timeout check
            int timedOut = 0;

            await foreach (var workflow in client.ListWorkflowsAsync(baseQuery, listOptions))
            {
                var timedOutStr = ExtractMemoValue(workflow.Memo, "timedOut");
                bool isTimedOut = !string.IsNullOrEmpty(timedOutStr) && 
                                  (timedOutStr.ToLower() == "true" || timedOutStr == "True");
                if (isTimedOut)
                {
                    timedOut++;
                }
            }

            return timedOut;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get timed out count, returning 0");
            return 0;
        }
    }

    /// <summary>
    /// Fallback method to get task statistics by listing all workflows.
    /// Used when CountWorkflow API is not available.
    /// </summary>
    private async Task<ServiceResult<TaskStatisticsResponse>> GetTaskStatisticsUsingList(ITemporalClient client, string baseQuery)
    {
        var listOptions = new WorkflowListOptions
        {
            Limit = 10000 // Set a reasonable limit for statistics gathering
        };

        // Initialize counters
        int pending = 0;
        int completed = 0;
        int timedOut = 0;
        int cancelled = 0;
        int total = 0;

        await foreach (var workflow in client.ListWorkflowsAsync(baseQuery, listOptions))
        {
            total++;

            // Categorize based on workflow status and timedOut flag
            var status = workflow.Status.ToString();
            
            // Check if task timed out (from memo)
            var timedOutStr = ExtractMemoValue(workflow.Memo, "timedOut");
            bool isTimedOut = !string.IsNullOrEmpty(timedOutStr) && 
                              (timedOutStr.ToLower() == "true" || timedOutStr == "True");

            if (isTimedOut)
            {
                timedOut++;
            }
            else if (status == "Running")
            {
                pending++;
            }
            else if (status == "Completed")
            {
                completed++;
            }
            else 
            {
                cancelled++;
            }
        }

        var response = new TaskStatisticsResponse
        {
            Pending = pending,
            Completed = completed,
            TimedOut = timedOut,
            Cancelled = cancelled,
            Total = total
        };

        _logger.LogInformation(
            "Task statistics retrieved using list method - Total: {Total}, Pending: {Pending}, Completed: {Completed}, TimedOut: {TimedOut}, Cancelled: {Cancelled}",
            total, pending, completed, timedOut, cancelled);

        return ServiceResult<TaskStatisticsResponse>.Success(response);
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
            return memoValue.Payload.Data?.ToStringUtf8()?.Replace("\"", "");
        }
        return null;
    }
}
