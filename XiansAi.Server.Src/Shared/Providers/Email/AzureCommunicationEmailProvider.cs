using Azure;
using Azure.Communication.Email;

namespace XiansAi.Server.Providers;

/// <summary>
/// Azure Communication Services implementation of the email provider
/// </summary>
public class AzureCommunicationEmailProvider : IEmailProvider, IEmailProviderRegistration
{
    private readonly EmailClient? _emailClient;
    private readonly string? _senderAddress;
    private readonly ILogger<AzureCommunicationEmailProvider> _logger;

    /// <summary>
    /// Gets the name of this provider
    /// </summary>
    public static string ProviderName => "AzureCommunicationServices";

    /// <summary>
    /// Gets the priority of this provider (lower numbers = higher priority)
    /// </summary>
    public static int Priority => 1;

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
    /// Determines if this provider can be registered with the given configuration
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>True if the provider can be registered, false otherwise</returns>
    public static bool CanRegister(IConfiguration configuration)
    {
        var connectionString = configuration["CommunicationServices:ConnectionString"];
        var senderEmail = configuration["CommunicationServices:SenderEmail"];
        return !string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(senderEmail);
    }

    /// <summary>
    /// Registers the services required by this provider
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["CommunicationServices:ConnectionString"];
        var senderEmail = configuration["CommunicationServices:SenderEmail"];

        if (!string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(senderEmail))
        {
            services.AddSingleton<EmailClient>(sp => new EmailClient(connectionString));
        }
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
                _logger.LogError("Email service is not properly configured. Please check CommunicationServices settings in configuration.");
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