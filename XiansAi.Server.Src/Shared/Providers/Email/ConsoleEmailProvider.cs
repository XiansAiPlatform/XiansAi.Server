namespace Shared.Providers;

/// <summary>
/// Console implementation of the email provider for development/testing purposes
/// </summary>
public class ConsoleEmailProvider : IEmailProvider
{
    private readonly ILogger<ConsoleEmailProvider> _logger;

    /// <summary>
    /// Creates a new instance of the ConsoleEmailProvider
    /// </summary>
    /// <param name="logger">Logger for the provider</param>
    public ConsoleEmailProvider(ILogger<ConsoleEmailProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends an email message by logging it to the console
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Always returns true for console provider</returns>
    public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        return await SendEmailAsync(new[] { to }, subject, body, isHtml);
    }

    /// <summary>
    /// Sends an email message to multiple recipients by logging it to the console
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>Always returns true for console provider</returns>
    public async Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        await Task.Delay(1); // Simulate async operation

        var recipients = string.Join(", ", to);
        var contentType = isHtml ? "HTML" : "Plain Text";

        _logger.LogInformation("=== EMAIL SENT (Console Provider) ===");
        _logger.LogInformation("To: {Recipients}", recipients);
        _logger.LogInformation("Subject: {Subject}", subject);
        _logger.LogInformation("Content Type: {ContentType}", contentType);
        _logger.LogInformation("Body: {Body}", body);
        _logger.LogInformation("=====================================");

        return true;
    }
} 