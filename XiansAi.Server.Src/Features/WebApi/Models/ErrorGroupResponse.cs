namespace Features.WebApi.Models;

/// <summary>
/// Represents an agent with its workflow critical log groupings
/// </summary>
public class AgentCriticalGroup
{
    /// <summary>
    /// Agent name
    /// </summary>
    public string AgentName { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflow types with critical logs grouped by workflow type
    /// </summary>
    public List<WorkflowTypeCriticalGroup> WorkflowTypes { get; set; } = new();
}

/// <summary>
/// Represents a workflow type with its workflow critical log groupings
/// </summary>
public class WorkflowTypeCriticalGroup
{
    /// <summary>
    /// Workflow type name
    /// </summary>
    public string WorkflowTypeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflows grouped by workflow ID
    /// </summary>
    public List<WorkflowCriticalGroup> Workflows { get; set; } = new();
}

/// <summary>
/// Represents a workflow with its workflow run critical log groupings
/// </summary>
public class WorkflowCriticalGroup
{
    /// <summary>
    /// Workflow ID
    /// </summary>
    public string WorkflowId { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflow runs with critical logs
    /// </summary>
    public List<WorkflowRunCriticalGroup> WorkflowRuns { get; set; } = new();
}

/// <summary>
/// Represents a workflow run with its last critical log
/// </summary>
public class WorkflowRunCriticalGroup
{
    /// <summary>
    /// Workflow run ID
    /// </summary>
    public string WorkflowRunId { get; set; } = string.Empty;
    
    /// <summary>
    /// Critical logs for this workflow run
    /// </summary>
    public List<Log> CriticalLogs { get; set; } = new();
} 