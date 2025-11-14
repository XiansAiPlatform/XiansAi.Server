using System.ComponentModel.DataAnnotations;

namespace Shared.Models.Schedule;

/// <summary>
/// Represents a Temporal schedule with agent metadata
/// </summary>
public class ScheduleModel
{
    /// <summary>
    /// Unique identifier for the schedule
    /// </summary>
    public required string Id { get; set; }
    
    /// <summary>
    /// Name of the agent that owns this schedule
    /// </summary>
    [StringLength(100)]
    public required string AgentName { get; set; }
    
    /// <summary>
    /// Type of workflow this schedule executes
    /// </summary>
    [StringLength(200)]
    public required string WorkflowType { get; set; }
    
    /// <summary>
    /// Schedule specification (cron expression or interval)
    /// </summary>
    public required string ScheduleSpec { get; set; }
    
    /// <summary>
    /// Next scheduled execution time
    /// </summary>
    public DateTime NextRunTime { get; set; }
    
    /// <summary>
    /// When the schedule was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Current status of the schedule
    /// </summary>
    public ScheduleStatus Status { get; set; }
    
    /// <summary>
    /// Additional metadata for schedule management
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    /// <summary>
    /// Human-readable description of the schedule
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Last execution time (if any)
    /// </summary>
    public DateTime? LastRunTime { get; set; }
    
    /// <summary>
    /// Number of executions completed
    /// </summary>
    public long ExecutionCount { get; set; }
}

/// <summary>
/// Possible states of a schedule
/// </summary>
public enum ScheduleStatus
{
    Active,
    Paused,
    Completed,
    Failed,
    Deleted
}