using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Shared.Utils;

namespace Shared.Providers;

/// <summary>
/// Sends email through any SMTP server. Stateless: the settings are passed on every call, so the
/// same instance serves the server default and every tenant override.
/// </summary>
public interface ISmtpEmailSender
{
    Task SendAsync(SmtpSettings settings, IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken cancellationToken = default);
}

/// <summary>
/// MailKit implementation of <see cref="ISmtpEmailSender"/>.
/// </summary>
public class SmtpEmailSender : ISmtpEmailSender
{
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(ILogger<SmtpEmailSender> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SendAsync(SmtpSettings settings, IEnumerable<string> to, string subject, string body, bool isHtml, CancellationToken cancellationToken = default)
    {
        var message = BuildMessage(settings, to, subject, body, isHtml);

        using var client = new SmtpClient { Timeout = (int)ClientTimeout.TotalMilliseconds };

        await client.ConnectAsync(settings.Host, settings.Port, ToSecureSocketOptions(settings.Security), cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            await client.DisconnectAsync(true, cancellationToken);
        }

        _logger.LogInformation("Email sent via SMTP {Host}:{Port} to {RecipientCount} recipient(s)",
            LogSanitizer.Sanitize(settings.Host), settings.Port, message.To.Count);
    }

    private static MimeMessage BuildMessage(SmtpSettings settings, IEnumerable<string> to, string subject, string body, bool isHtml)
    {
        var recipients = to?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? [];
        if (recipients.Count == 0)
            throw new ArgumentException("At least one recipient is required", nameof(to));

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.SenderName ?? string.Empty, settings.SenderEmail));
        foreach (var recipient in recipients)
        {
            if (!SmtpSettings.IsValidEmail(recipient))
                throw new ArgumentException($"Invalid recipient email address: {recipient}", nameof(to));
            message.To.Add(MailboxAddress.Parse(recipient));
        }
        message.Subject = subject;
        message.Body = new TextPart(isHtml ? "html" : "plain") { Text = body };
        return message;
    }

    private static SecureSocketOptions ToSecureSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto
    };
}
