using XiansAi.Server.Providers;

namespace Shared.Services;

/// <summary>
/// Email service that uses the provider pattern for flexible email implementations
/// </summary>
public class EmailService : IEmailService
{
    private readonly IEmailProvider _emailProvider;
    private readonly ILogger<EmailService> _logger;

    /// <summary>
    /// Creates a new instance of the EmailService
    /// </summary>
    /// <param name="emailProvider">Factory for creating email providers</param>
    /// <param name="logger">Logger for the service</param>
    public EmailService(
        IEmailProvider emailProvider,
        ILogger<EmailService> logger)
    {
        _emailProvider = emailProvider ?? throw new ArgumentNullException(nameof(emailProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends an email message
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Task representing the async operation</returns>
    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        try
        {
            var success = await _emailProvider.SendEmailAsync(to, subject, body, isHtml);
            if (!success)
            {
                throw new InvalidOperationException("Failed to send email through the configured provider.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {Recipient} with subject '{Subject}'", to, subject);
            throw new Exception("Error sending email", ex);
        }
    }

    /// <summary>
    /// Sends an email message to multiple recipients
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Task representing the async operation</returns>
    public async Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        try
        {
            var success = await _emailProvider.SendEmailAsync(to, subject, body, isHtml);
            if (!success)
            {
                throw new InvalidOperationException("Failed to send email through the configured provider.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {RecipientCount} recipients with subject '{Subject}'", to.Count(), subject);
            throw new Exception("Error sending email", ex);
        }
    }
}

/// <summary>
/// Interface for email service operations
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email message
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);

    /// <summary>
    /// Sends an email message to multiple recipients
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Task representing the async operation</returns>
    Task SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
}
