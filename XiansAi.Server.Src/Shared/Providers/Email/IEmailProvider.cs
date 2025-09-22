namespace Shared.Providers;

/// <summary>
/// Interface for email providers that abstracts email implementation details
/// </summary>
public interface IEmailProvider
{
    /// <summary>
    /// Sends an email message
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully, false otherwise</returns>
    Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false);

    /// <summary>
    /// Sends an email message to multiple recipients
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully to all recipients, false otherwise</returns>
    Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false);
} 