namespace Shared.Models.Schedule;

/// <summary>
/// Represents an upcoming or past schedule execution
/// </summary>
public class ScheduleRunModel
{
    /// <summary>
    /// Unique identifier for this run
    /// </summary>
    public required string RunId { get; set; }
    
    /// <summary>
    /// Schedule this run belongs to
    /// </summary>
    public required string ScheduleId { get; set; }
    
    /// <summary>
    /// Scheduled execution time
    /// </summary>
    public DateTime ScheduledTime { get; set; }
    
    /// <summary>
    /// Actual execution time (null if not yet executed)
    /// </summary>
    public DateTime? ActualRunTime { get; set; }
    
    /// <summary>
    /// Current status of this run
    /// </summary>
    public ScheduleRunStatus Status { get; set; }
    
    /// <summary>
    /// Workflow run ID if execution started
    /// </summary>
    public string? WorkflowRunId { get; set; }
    
    /// <summary>
    /// Error details if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Possible states of a schedule run
/// </summary>
public enum ScheduleRunStatus
{
    Scheduled,
    Running,
    Completed,
    Failed,
    Skipped
}