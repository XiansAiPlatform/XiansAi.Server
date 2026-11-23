using System.Text;

namespace Shared.Services;

/// <summary>
/// What an agent reports when one of its plugin runs fails.
/// </summary>
public sealed record PluginFailureNotification
{
    public string? AgentName { get; init; }
    public List<string>? Plugins { get; init; }
    public string? Repository { get; init; }
    /// <summary>What started the run, e.g. "PR #42 (webhook 'pull requests')".</summary>
    public string? Trigger { get; init; }
    public string? ExecutionId { get; init; }
    public string? Error { get; init; }
    public DateTime? OccurredAt { get; init; }
}

/// <summary>
/// Turns a <see cref="PluginFailureNotification"/> into the plain-text email sent to the tenant's contacts.
/// </summary>
public static class PluginFailureEmail
{
    internal const int MaxErrorLength = 4000;
    private const int MaxFieldLength = 500;
    private const int MaxSubjectLength = 200;

    public static string BuildSubject(PluginFailureNotification notification)
    {
        var plugins = PluginList(notification);
        var subject = plugins.Length == 0 ? "[Xians] A plugin run failed" : $"[Xians] Plugin failed: {plugins}";
        if (!string.IsNullOrWhiteSpace(notification.Repository))
            subject += $" ({Clean(notification.Repository)})";

        return subject.Length > MaxSubjectLength ? subject[..MaxSubjectLength] + "…" : subject;
    }

    public static string BuildBody(string tenantId, PluginFailureNotification notification)
    {
        var body = new StringBuilder();
        body.AppendLine("A plugin run failed and did not complete.");
        body.AppendLine();

        AppendField(body, "Tenant", tenantId);
        AppendField(body, "Agent", notification.AgentName);
        AppendField(body, "Plugin(s)", PluginList(notification));
        AppendField(body, "Repository", notification.Repository);
        AppendField(body, "Trigger", notification.Trigger);
        AppendField(body, "Execution ID", notification.ExecutionId);
        AppendField(body, "Time (UTC)", (notification.OccurredAt ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"));

        var error = string.IsNullOrWhiteSpace(notification.Error)
            ? "No error details were reported."
            : notification.Error.Trim();
        // The root cause is usually at the end of a message, so keep the tail.
        if (error.Length > MaxErrorLength)
            error = "…" + error[^MaxErrorLength..];

        body.AppendLine();
        body.AppendLine("Error:");
        body.AppendLine(error);
        body.AppendLine();
        body.AppendLine("You are receiving this because you are listed as a notification contact for this tenant.");
        return body.ToString();
    }

    private static string PluginList(PluginFailureNotification notification) =>
        Clean(string.Join(", ", (notification.Plugins ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())));

    private static void AppendField(StringBuilder body, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            body.AppendLine($"{label}: {Clean(value)}");
    }

    private static string Clean(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length > MaxFieldLength ? value[..MaxFieldLength] + "…" : value;
    }
}
