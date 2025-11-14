namespace Shared.Models.Schedule;

/// <summary>
/// Request model for filtering schedules
/// </summary>
public class ScheduleFilterRequest
{
    /// <summary>
    /// Filter by agent name (optional)
    /// </summary>
    public string? AgentName { get; set; }
    
    /// <summary>
    /// Filter by workflow type (optional)
    /// </summary>
    public string? WorkflowType { get; set; }
    
    /// <summary>
    /// Filter by schedule status (optional)
    /// </summary>
    public ScheduleStatus? Status { get; set; }
    
    /// <summary>
    /// Page size for pagination
    /// </summary>
    public int PageSize { get; set; } = 20;
    
    /// <summary>
    /// Page token for pagination
    /// </summary>
    public string? PageToken { get; set; }
    
    /// <summary>
    /// Search term for description/name
    /// </summary>
    public string? SearchTerm { get; set; }
}