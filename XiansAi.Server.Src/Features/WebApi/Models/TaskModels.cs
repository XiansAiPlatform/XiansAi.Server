namespace Features.WebApi.Models;

/// <summary>
/// Response model for task information.
/// </summary>
public class TaskInfoResponse
{
    public required string WorkflowId { get; set; }
    public required string RunId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? InitialWork { get; set; }
    public string? FinalWork { get; set; }
    public string? ParticipantId { get; set; }
    public required string Status { get; set; }
    public required bool IsCompleted { get; set; }
    
    /// <summary>
    /// Available actions for this task (e.g., ["approve", "reject", "hold"]).
    /// </summary>
    public string[]? AvailableActions { get; set; }
    
    /// <summary>
    /// The action that was performed (if completed).
    /// </summary>
    public string? PerformedAction { get; set; }
    
    /// <summary>
    /// Comment provided with the action.
    /// </summary>
    public string? Comment { get; set; }
    
    public DateTime? StartTime { get; set; }
    public DateTime? CloseTime { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request model for updating task draft.
/// </summary>
public class UpdateDraftRequest
{
    public required string UpdatedDraft { get; set; }
}

/// <summary>
/// Request model for performing an action on a task.
/// </summary>
public class PerformActionRequest
{
    /// <summary>
    /// The action to perform (should be one of the task's available actions).
    /// </summary>
    public required string Action { get; set; }
    
    /// <summary>
    /// Optional comment for the action.
    /// </summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Response model for paginated tasks list.
/// </summary>
public class PaginatedTasksResponse
{
    public required List<TaskInfoResponse> Tasks { get; set; }
    public string? NextPageToken { get; set; }
    public required int PageSize { get; set; }
    public required bool HasNextPage { get; set; }
    public int? TotalCount { get; set; }
}
