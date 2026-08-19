namespace Shared.Utils;

/// <summary>
/// Well-known built-in workflow names and the Admin API default when
/// <c>workflowType</c> is omitted.
/// </summary>
public static class WorkflowTypeDefaults
{
    public const string Supervisor = "Supervisor Workflow";

    public static string EffectiveName(string? workflowType)
        => string.IsNullOrWhiteSpace(workflowType) ? Supervisor : workflowType.Trim();
}
