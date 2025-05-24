namespace Features.WebApi.Models;

/// <summary>
/// Represents a workflow response object containing workflow execution details.
/// </summary>
public class WorkflowResponse
{
    /// <summary>
    /// Gets or sets the agent associated with the workflow.
    /// </summary>
    public string? Agent { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier associated with the workflow.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the owner of the workflow.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the workflow identifier.
    /// </summary>
    public string? WorkflowId { get; set; }

    /// <summary>
    /// Gets or sets the run identifier for the workflow execution.
    /// </summary>
    public string? RunId { get; set; }

    /// <summary>
    /// Gets or sets the type of the workflow.
    /// </summary>
    public string? WorkflowType { get; set; }

    /// <summary>
    /// Gets or sets the current status of the workflow.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the number of workers associated with the workflow.
    /// </summary>
    public string? NumOfWorkers { get; set; }

    /// <summary>
    /// Gets or sets the task queue associated with the workflow.
    /// </summary>
    public string? TaskQueue { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow started.
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Gets or sets the execution time of the workflow.
    /// </summary>
    public DateTime? ExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow closed.
    /// </summary>
    public DateTime? CloseTime { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the parent workflow, if any.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the run identifier of the parent workflow, if any.
    /// </summary>
    public string? ParentRunId { get; set; }

    /// <summary>
    /// Gets or sets the history length of the workflow.
    /// </summary>
    public int HistoryLength { get; set; }

    /// <summary>
    /// Gets or sets the current activity associated with the workflow.
    /// </summary>
    public object? CurrentActivity { get; set; }

    /// <summary>
    /// Gets or sets the last log associated with the workflow.
    /// </summary>
    public object? LastLog { get; set; }
} 