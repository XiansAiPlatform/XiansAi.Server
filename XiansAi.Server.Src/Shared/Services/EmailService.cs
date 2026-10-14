using System.Text.Json.Serialization;
using Shared.Providers;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Where a tenant's email is sent from.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailSource
{
    /// <summary>Neither the tenant nor the server has an email server configured.</summary>
    None,
    /// <summary>The server default SMTP server (<c>Email:Smtp</c>).</summary>
    Server,
    /// <summary>The tenant's own SMTP server (overrides the server default).</summary>
    Tenant
}

/// <summary>
/// Sends email on behalf of a tenant. Other components use this to notify users and admins.
/// <list type="bullet">
/// <item>The tenant has its own SMTP config → it is used.</item>
/// <item>Otherwise the server default SMTP server (<c>Email:Smtp</c>) is used.</item>
/// <item>Neither is configured → email is off; sends are skipped and logged, not failed.</item>
/// </list>
/// </summary>
public interface IEmailService
{
    /// <summary>Returns which SMTP server the tenant's email is sent through.</summary>
    /// <param name="tenantId">The tenant, or null for platform-level email (server default only).</param>
    Task<EmailSource> GetSourceAsync(string? tenantId);

    /// <summary>Whether email will be sent for the tenant.</summary>
    Task<bool> IsEnabledAsync(string? tenantId);

    /// <summary>Sends an email for the tenant. Does nothing when email is off.</summary>
    /// <param name="tenantId">The tenant, or null for platform-level email (server default only).</param>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    Task SendEmailAsync(string? tenantId, string to, string subject, string body, bool isHtml = false);

    /// <summary>Sends an email to multiple recipients for the tenant. Does nothing when email is off.</summary>
    /// <param name="tenantId">The tenant, or null for platform-level email (server default only).</param>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    Task SendEmailAsync(string? tenantId, IEnumerable<string> to, string subject, string body, bool isHtml = false);
}

/// <summary>
/// Default <see cref="IEmailService"/>: tenant SMTP override, then the server default SMTP server.
/// </summary>
public class EmailService : IEmailService
{
    private readonly ITenantSmtpConfigRepository _tenantSmtpConfigRepository;
    private readonly ISmtpEmailSender _smtpSender;
    private readonly SmtpSettings? _serverSmtp;
    private readonly ILogger<EmailService> _logger;

    /// <summary>
    /// Creates a new instance of the EmailService
    /// </summary>
    /// <param name="tenantSmtpConfigRepository">Per-tenant SMTP overrides</param>
    /// <param name="smtpSender">Sends through an SMTP server</param>
    /// <param name="logger">Logger for the service</param>
    /// <param name="serverSmtp">Server default SMTP server; null when <c>Email:Smtp:Host</c> is not set</param>
    public EmailService(
        ITenantSmtpConfigRepository tenantSmtpConfigRepository,
        ISmtpEmailSender smtpSender,
        ILogger<EmailService> logger,
        SmtpSettings? serverSmtp = null)
    {
        _tenantSmtpConfigRepository = tenantSmtpConfigRepository ?? throw new ArgumentNullException(nameof(tenantSmtpConfigRepository));
        _smtpSender = smtpSender ?? throw new ArgumentNullException(nameof(smtpSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverSmtp = serverSmtp;
    }

    public async Task<EmailSource> GetSourceAsync(string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId) && await _tenantSmtpConfigRepository.GetAsync(tenantId) != null)
        {
            return EmailSource.Tenant;
        }

        return _serverSmtp != null ? EmailSource.Server : EmailSource.None;
    }

    public async Task<bool> IsEnabledAsync(string? tenantId)
    {
        return await GetSourceAsync(tenantId) != EmailSource.None;
    }

    public Task SendEmailAsync(string? tenantId, string to, string subject, string body, bool isHtml = false)
    {
        return SendEmailAsync(tenantId, new[] { to }, subject, body, isHtml);
    }

    public async Task SendEmailAsync(string? tenantId, IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        var recipients = to.ToList();
        try
        {
            var tenantConfig = string.IsNullOrWhiteSpace(tenantId)
                ? null
                : await _tenantSmtpConfigRepository.GetAsync(tenantId);

            var settings = tenantConfig?.ToSettings() ?? _serverSmtp;
            if (settings == null)
            {
                _logger.LogInformation("Email '{Subject}' for tenant {TenantId} was not sent: no email server is configured",
                    LogSanitizer.Sanitize(subject), LogSanitizer.Sanitize(tenantId));
                return;
            }

            await _smtpSender.SendAsync(settings, recipients, subject, body, isHtml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email for tenant {TenantId} to {RecipientCount} recipient(s) with subject '{Subject}'",
                LogSanitizer.Sanitize(tenantId), recipients.Count, LogSanitizer.Sanitize(subject));
            throw new Exception("Error sending email", ex);
        }
    }
}
