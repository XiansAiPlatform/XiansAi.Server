using Shared.Auth;
using Shared.Utils;
using Temporalio.Client;
using Shared.Utils.Services;
using Features.WebApi.Models;
using Shared.Utils.Temporal;
using Shared.Repositories;
using Shared.Services;

namespace Features.WebApi.Services;

public interface ITaskService
{
    Task<ServiceResult<TaskInfoResponse>> GetTaskById(string workflowId);
    Task<ServiceResult<object>> UpdateDraft(string workflowId, string updatedDraft);
    Task<ServiceResult<object>> PerformAction(string workflowId, string action, string? comment);
    Task<ServiceResult<PaginatedTasksResponse>> GetTasks(int? pageSize, string? pageToken, string? agent, string? participantId, string? status);
}

/// <summary>
/// Service for managing human-in-the-loop task workflows.
/// Provides methods to query, update, and perform actions on tasks.
/// </summary>
public class TaskService : ITaskService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<TaskService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentRepository _agentRepository;
    private readonly IPermissionsService _permissionsService;
    private readonly IAdminTaskService _adminTaskService;

    public TaskService(
        ITemporalClientFactory clientFactory,
        ILogger<TaskService> logger,
        ITenantContext tenantContext,
        IAgentRepository agentRepository,
        IPermissionsService permissionsService,
        IAdminTaskService adminTaskService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
        _adminTaskService = adminTaskService ?? throw new ArgumentNullException(nameof(adminTaskService));
    }

    /// <summary>
    /// Retrieves task information by workflow ID.
    /// Delegates to the shared AdminTaskService implementation and maps the result.
    /// </summary>
    public async Task<ServiceResult<TaskInfoResponse>> GetTaskById(string workflowId)
    {
        var adminResult = await _adminTaskService.GetTaskById(workflowId);
        
        if (!adminResult.IsSuccess)
        {
            // Map error response to TaskInfoResponse type while preserving status code
            return adminResult.StatusCode switch
            {
                StatusCode.BadRequest => ServiceResult<TaskInfoResponse>.BadRequest(adminResult.ErrorMessage ?? "Failed to retrieve task"),
                StatusCode.NotFound => ServiceResult<TaskInfoResponse>.NotFound(adminResult.ErrorMessage ?? "Task not found"),
                StatusCode.Forbidden => ServiceResult<TaskInfoResponse>.Forbidden(adminResult.ErrorMessage ?? "Access forbidden"),
                StatusCode.Unauthorized => ServiceResult<TaskInfoResponse>.Unauthorized(adminResult.ErrorMessage ?? "Unauthorized"),
                _ => ServiceResult<TaskInfoResponse>.InternalServerError(adminResult.ErrorMessage ?? "Failed to retrieve task")
            };
        }

        var adminData = adminResult.Data!;
        
        // Map AdminTaskInfoResponse to TaskInfoResponse (exclude admin-specific fields)
        var response = new TaskInfoResponse
        {
            WorkflowId = adminData.WorkflowId,
            RunId = adminData.RunId,
            Title = adminData.Title,
            Description = adminData.Description,
            InitialWork = adminData.InitialWork,
            FinalWork = adminData.FinalWork,
            ParticipantId = adminData.ParticipantId,
            Status = adminData.Status,
            IsCompleted = adminData.IsCompleted,
            AvailableActions = adminData.AvailableActions,
            PerformedAction = adminData.PerformedAction,
            Comment = adminData.Comment,
            StartTime = adminData.StartTime,
            CloseTime = adminData.CloseTime,
            Metadata = adminData.Metadata
        };

        return ServiceResult<TaskInfoResponse>.Success(response);
    }

    /// <summary>
    /// Updates the draft work for a task.
    /// Delegates to the shared AdminTaskService implementation.
    /// </summary>
    public async Task<ServiceResult<object>> UpdateDraft(string workflowId, string updatedDraft)
    {
        return await _adminTaskService.UpdateDraft(workflowId, updatedDraft);
    }

    /// <summary>
    /// Performs an action on a task with an optional comment.
    /// Delegates to the shared AdminTaskService implementation.
    /// </summary>
    public async Task<ServiceResult<object>> PerformAction(string workflowId, string action, string? comment)
    {
        return await _adminTaskService.PerformAction(workflowId, action, comment);
    }

    /// <summary>
    /// Retrieves a paginated list of tasks with optional filtering.
    /// </summary>
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
    /// Extracts available actions from the workflow memo.
    /// </summary>
    private TaskInfoResponse MapWorkflowToTaskInfo(WorkflowExecution workflow)
    {
        var taskTitle = ExtractMemoValue(workflow.Memo, Constants.TaskTitleKey) ?? "Untitled Task";
        var taskDescription = ExtractMemoValue(workflow.Memo, Constants.TaskDescriptionKey) ?? "";
        var participantId = ExtractMemoValue(workflow.Memo, Constants.UserIdKey);
        var taskActionsStr = ExtractMemoValue(workflow.Memo, Constants.TaskActionsKey);

        // Parse available actions from comma-separated string
        string[]? availableActions = null;
        if (!string.IsNullOrEmpty(taskActionsStr))
        {
            availableActions = taskActionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return new TaskInfoResponse
        {
            WorkflowId = workflow.Id,
            RunId = workflow.RunId,
            Title = taskTitle,
            Description = taskDescription,
            ParticipantId = participantId,
            Status = workflow.Status.ToString(),
            IsCompleted = workflow.Status != Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Running,
            AvailableActions = availableActions,
            StartTime = workflow.StartTime,
            CloseTime = workflow.CloseTime
        };
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
