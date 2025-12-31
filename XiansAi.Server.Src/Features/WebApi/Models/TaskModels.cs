namespace Features.WebApi.Models;

/// <summary>
/// Response model for task information.
/// </summary>
public class TaskInfoResponse
{
    public required string TaskId { get; set; }
    public required string WorkflowId { get; set; }
    public required string RunId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? CurrentDraft { get; set; }
    public string? ParticipantId { get; set; }
    public required string Status { get; set; }
    public required bool IsCompleted { get; set; }
    public string? Error { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? CloseTime { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Request model for updating task draft.
/// </summary>
public class 
UpdateDraftRequest
{
    public required string UpdatedDraft { get; set; }
}

/// <summary>
/// Request model for rejecting a task.
/// </summary>
public class RejectTaskRequest
{
    public required string RejectionMessage { get; set; }
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

