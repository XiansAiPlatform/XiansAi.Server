using System.Collections.Generic;

namespace XiansAi.Server.Features.WebApi.Models;

/// <summary>
/// Represents an agent with its workflow error groupings
/// </summary>
public class AgentErrorGroup
{
    /// <summary>
    /// Agent name
    /// </summary>
    public string AgentName { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflow types with errors grouped by workflow type
    /// </summary>
    public List<WorkflowTypeErrorGroup> WorkflowTypes { get; set; } = new();
}

/// <summary>
/// Represents a workflow type with its workflow error groupings
/// </summary>
public class WorkflowTypeErrorGroup
{
    /// <summary>
    /// Workflow type name
    /// </summary>
    public string WorkflowTypeName { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflows grouped by workflow ID
    /// </summary>
    public List<WorkflowErrorGroup> Workflows { get; set; } = new();
}

/// <summary>
/// Represents a workflow with its workflow run error groupings
/// </summary>
public class WorkflowErrorGroup
{
    /// <summary>
    /// Workflow ID
    /// </summary>
    public string WorkflowId { get; set; } = string.Empty;
    
    /// <summary>
    /// Workflow runs with error logs
    /// </summary>
    public List<WorkflowRunErrorGroup> WorkflowRuns { get; set; } = new();
}

/// <summary>
/// Represents a workflow run with its last error log
/// </summary>
public class WorkflowRunErrorGroup
{
    /// <summary>
    /// Workflow run ID
    /// </summary>
    public string WorkflowRunId { get; set; } = string.Empty;
    
    /// <summary>
    /// Error logs for this workflow run
    /// </summary>
    public List<Log> ErrorLogs { get; set; } = new();
} 