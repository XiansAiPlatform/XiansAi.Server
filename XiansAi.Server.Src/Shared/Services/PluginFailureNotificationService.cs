using System.Text;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// What an agent reports when one of its plugin runs fails.
/// </summary>
public sealed record PluginFailureNotification
{
    public string? AgentName { get; init; }
    public List<string>? Plugins { get; init; }
    public string? Repository { get; init; }
    /// <summary>What started the run, e.g. "PR #42" or "schedule nightly-scan".</summary>
    public string? Trigger { get; init; }
    public string? ExecutionId { get; init; }
    public string? Error { get; init; }
    public DateTime? OccurredAt { get; init; }
}

public interface IPluginFailureNotificationService
{
    /// <summary>
    /// Emails every contact of the tenant about a failed plugin run.
    /// Returns how many contacts were emailed (0 when the tenant has no contacts or email is off).
    /// </summary>
    Task<ServiceResult<int>> NotifyAsync(string tenantId, PluginFailureNotification notification);
}

/// <summary>
/// Sends plugin failure alerts to the tenant's notification contacts through <see cref="IEmailService"/>.
/// Each contact gets their own email so recipients never see each other's addresses.
/// </summary>
public class PluginFailureNotificationService : IPluginFailureNotificationService
{
    internal const int MaxErrorLength = 4000;
    private const int MaxFieldLength = 500;

    private readonly IContactRepository _contactRepository;
    private readonly IEmailService _emailService;
    private readonly ILogger<PluginFailureNotificationService> _logger;

    public PluginFailureNotificationService(
        IContactRepository contactRepository,
        IEmailService emailService,
        ILogger<PluginFailureNotificationService> logger)
    {
        _contactRepository = contactRepository;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ServiceResult<int>> NotifyAsync(string tenantId, PluginFailureNotification notification)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<int>.BadRequest("Tenant context is not available");
        if (notification == null)
            return ServiceResult<int>.BadRequest("Request payload is required");

        if (!await _emailService.IsEnabledAsync(tenantId))
        {
            _logger.LogInformation("Plugin failure alert for tenant {TenantId} skipped: no email server is configured",
                LogSanitizer.Sanitize(tenantId));
            return ServiceResult<int>.Success(0);
        }

        var recipients = (await _contactRepository.ListAsync(tenantId))
            .Select(c => c.Email)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipients.Count == 0)
        {
            _logger.LogInformation("Plugin failure alert for tenant {TenantId} skipped: the tenant has no contacts",
                LogSanitizer.Sanitize(tenantId));
            return ServiceResult<int>.Success(0);
        }

        var subject = BuildSubject(notification);
        var body = BuildBody(tenantId, notification);

        var sent = 0;
        foreach (var recipient in recipients)
        {
            try
            {
                await _emailService.SendEmailAsync(tenantId, recipient, subject, body);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad address or a transient SMTP error must not stop the other contacts hearing
                // about it. Cancellation still propagates.
                _logger.LogWarning(ex, "Plugin failure alert for tenant {TenantId} could not be sent to a contact",
                    LogSanitizer.Sanitize(tenantId));
            }
        }

        if (sent == 0)
            return ServiceResult<int>.InternalServerError("The failure alert could not be sent to any contact");

        _logger.LogInformation("Plugin failure alert for tenant {TenantId} sent to {Sent}/{Total} contact(s)",
            LogSanitizer.Sanitize(tenantId), sent, recipients.Count);
        return ServiceResult<int>.Success(sent);
    }

    internal static string BuildSubject(PluginFailureNotification notification)
    {
        var plugins = PluginList(notification);
        var subject = plugins.Length == 0 ? "[Xians] A plugin run failed" : $"[Xians] Plugin failed: {plugins}";
        if (!string.IsNullOrWhiteSpace(notification.Repository))
            subject += $" ({Clean(notification.Repository)})";

        // Header injection guard: a subject must stay on one line.
        subject = subject.Replace('\r', ' ').Replace('\n', ' ');
        return subject.Length > 200 ? subject[..200] + "…" : subject;
    }

    internal static string BuildBody(string tenantId, PluginFailureNotification notification)
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
        // The root cause is usually at the end of a log or traceback, so keep the tail.
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
