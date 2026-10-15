using System.Globalization;
using System.Text;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services.HealthChecks;

public enum HealthCheckNotificationKind
{
    None,

    /// <summary>The check was passing (or never ran) and this run failed.</summary>
    Failing,

    /// <summary>The check was failing and this run passed.</summary>
    Recovered
}

/// <summary>
/// Decides when to send an email. Emails are only sent when a check starts failing or passes again,
/// not after every failed run. Fail and Error both count as failing.
/// </summary>
public static class HealthCheckNotificationPolicy
{
    public static HealthCheckNotificationKind Decide(HealthCheckStatus? previous, HealthCheckStatus current)
    {
        var wasFailing = previous is HealthCheckStatus.Fail or HealthCheckStatus.Error;
        var isFailing = current is HealthCheckStatus.Fail or HealthCheckStatus.Error;

        if (isFailing && !wasFailing) return HealthCheckNotificationKind.Failing;
        if (!isFailing && wasFailing) return HealthCheckNotificationKind.Recovered;
        return HealthCheckNotificationKind.None;
    }
}

public interface IHealthCheckNotifier
{
    /// <summary>
    /// Emails the check's contacts when the run changes the check from passing to failing, or back.
    /// <paramref name="check"/> is the check as it was before the run.
    /// It never throws, so email problems don't affect the run.
    /// </summary>
    Task NotifyAsync(AgentHealthCheck check, AgentHealthCheckRun run);
}

public class HealthCheckNotifier : IHealthCheckNotifier
{
    private readonly IContactRepository _contactRepository;
    private readonly IEmailService _emailService;
    private readonly ILogger<HealthCheckNotifier> _logger;

    public HealthCheckNotifier(IContactRepository contactRepository, IEmailService emailService, ILogger<HealthCheckNotifier> logger)
    {
        _contactRepository = contactRepository;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task NotifyAsync(AgentHealthCheck check, AgentHealthCheckRun run)
    {
        if (check.NotifyContactIds.Count == 0)
            return;

        var kind = HealthCheckNotificationPolicy.Decide(check.LastStatus, run.Status);
        if (kind == HealthCheckNotificationKind.None)
            return;

        try
        {
            if (!await _emailService.IsEnabledAsync(check.TenantId))
            {
                _logger.LogInformation("Health check {CheckId} is {Kind} but email is off for the tenant; no notification sent",
                    LogSanitizer.Sanitize(check.Id), kind);
                return;
            }

            // Skip contacts that were deleted.
            var wanted = check.NotifyContactIds.ToHashSet(StringComparer.Ordinal);
            var contacts = (await _contactRepository.ListAsync(check.TenantId)).Where(c => wanted.Contains(c.Id)).ToList();

            var subject = BuildSubject(check, kind);
            var sent = 0;
            // Send one email per contact, so they don't see each other's addresses.
            foreach (var contact in contacts)
            {
                try
                {
                    await _emailService.SendEmailAsync(check.TenantId, contact.Email, subject, BuildBody(check, run, kind, contact.Name));
                    sent++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to email contact {ContactId} about health check {CheckId}",
                        LogSanitizer.Sanitize(contact.Id), LogSanitizer.Sanitize(check.Id));
                }
            }

            _logger.LogInformation("Health check {CheckId} is {Kind}: notified {Sent} of {Count} contact(s)",
                LogSanitizer.Sanitize(check.Id), kind, sent, contacts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notifications for health check {CheckId}", LogSanitizer.Sanitize(check.Id));
        }
    }

    public static string BuildSubject(AgentHealthCheck check, HealthCheckNotificationKind kind)
    {
        var state = kind == HealthCheckNotificationKind.Recovered ? "recovered" : "failing";
        return $"Eval {state}: {check.Name} ({check.AgentName} / {check.ActivationName})";
    }

    public static string BuildBody(AgentHealthCheck check, AgentHealthCheckRun run, HealthCheckNotificationKind kind, string contactName)
    {
        var body = new StringBuilder();
        body.AppendLine($"Hi {contactName},");
        body.AppendLine();

        if (kind == HealthCheckNotificationKind.Recovered)
        {
            var failedRuns = check.ConsecutiveFailures;
            body.AppendLine($"The eval \"{check.Name}\" on agent \"{check.AgentName}\" (activation \"{check.ActivationName}\") is passing again" +
                            (failedRuns > 0 ? $" after {failedRuns} failed run{(failedRuns == 1 ? "" : "s")}." : "."));
            body.AppendLine();
            AppendField(body, "Type", TypeLabel(check.Type));
            AppendField(body, "Passed at", FormatTime(run.StartedAt));
        }
        else
        {
            body.AppendLine($"The eval \"{check.Name}\" on agent \"{check.AgentName}\" (activation \"{check.ActivationName}\") is failing.");
            body.AppendLine();
            AppendField(body, "Result", run.Status == HealthCheckStatus.Error
                ? "Error: the agent could not be reached or did not answer"
                : "Failed: the agent answered, but a check did not pass");
            AppendField(body, "Type", TypeLabel(check.Type));
            AppendField(body, "Run at", FormatTime(run.StartedAt));
            if (!string.IsNullOrWhiteSpace(run.Detail))
                AppendField(body, "Detail", run.Detail);

            if (!string.IsNullOrWhiteSpace(run.ResponseSnippet))
            {
                body.AppendLine();
                body.AppendLine("Response (start):");
                body.AppendLine(run.ResponseSnippet);
            }

            body.AppendLine();
            body.AppendLine($"The eval keeps running every {FormatInterval(check.IntervalMinutes)}. You'll get one more email when it passes again.");
        }

        body.AppendLine();
        body.AppendLine("See the run history in Agent Studio under Agent Settings > Evals.");
        body.AppendLine();
        body.AppendLine("You are receiving this because you are a notification contact for this eval.");
        return body.ToString();
    }

    private static void AppendField(StringBuilder body, string label, string value) => body.AppendLine($"{label}: {value}");

    private static string TypeLabel(HealthCheckType type) => type switch
    {
        HealthCheckType.Chat => "Chat",
        HealthCheckType.Webhook => "Webhook",
        HealthCheckType.Schedule => "Schedule",
        _ => type.ToString()
    };

    private static string FormatTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatInterval(int minutes) => minutes switch
    {
        60 => "hour",
        _ when minutes % 60 == 0 => $"{minutes / 60} hours",
        _ => $"{minutes} minutes"
    };
}
