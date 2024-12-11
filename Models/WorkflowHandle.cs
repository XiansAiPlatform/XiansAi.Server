
/// <summary>
/// Interface representing a handle to a workflow.
/// </summary>
public interface IWorkflowHandle
{
    /// <summary>
    /// Gets or sets the workflow ID.
    /// </summary>
    string? Id { get; set; }
}

/// <summary>
/// Implementation of the IWorkflowHandle interface.
/// </summary>
public class WorkflowHandle : IWorkflowHandle
{
    /// <inheritdoc/>
    public string? Id { get; set; }
}