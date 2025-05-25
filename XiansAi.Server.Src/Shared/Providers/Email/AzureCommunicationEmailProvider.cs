using Azure;
using Azure.Communication.Email;

namespace XiansAi.Server.Providers;

/// <summary>
/// Azure Communication Services implementation of the email provider
/// </summary>
public class AzureCommunicationEmailProvider : IEmailProvider
{
    private readonly EmailClient? _emailClient;
    private readonly string? _senderAddress;
    private readonly ILogger<AzureCommunicationEmailProvider> _logger;

    /// <summary>
    /// Creates a new instance of the AzureCommunicationEmailProvider
    /// </summary>
    /// <param name="emailClient">The Azure Communication Services email client</param>
    /// <param name="senderAddress">The sender email address</param>
    /// <param name="logger">Logger for the provider</param>
    public AzureCommunicationEmailProvider(
        EmailClient? emailClient,
        string? senderAddress,
        ILogger<AzureCommunicationEmailProvider> logger)
    {
        _emailClient = emailClient;
        _senderAddress = senderAddress;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends an email message
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully, false otherwise</returns>
    public async Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        return await SendEmailAsync(new[] { to }, subject, body, isHtml);
    }

    /// <summary>
    /// Sends an email message to multiple recipients
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully to all recipients, false otherwise</returns>
    public async Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        try
        {
            if (_emailClient == null || string.IsNullOrEmpty(_senderAddress))
            {
                _logger.LogError("Email service is not properly configured. Please check Email:Azure settings in configuration.");
                return false;
            }

            var emailContent = new EmailContent(subject)
            {
                PlainText = !isHtml ? body : null,
                Html = isHtml ? body : null
            };

            var toRecipients = to.Select(email => new EmailAddress(email)).ToList();
            var emailRecipients = new EmailRecipients(toRecipients);
            
            var emailMessage = new EmailMessage(
                senderAddress: _senderAddress,
                recipients: emailRecipients,
                content: emailContent);

            var response = await _emailClient.SendAsync(
                wait: WaitUntil.Completed,
                message: emailMessage);

            if (response.Value.Status == EmailSendStatus.Succeeded)
            {
                _logger.LogInformation("Email sent successfully to {RecipientCount} recipients", toRecipients.Count);
                return true;
            }
            else
            {
                _logger.LogError("Failed to send email: {Status}", response.Value.Status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {RecipientCount} recipients", to.Count());
            return false;
        }
    }
} 