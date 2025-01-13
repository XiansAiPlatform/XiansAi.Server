using Azure;
using Azure.Communication.Email;


public class EmailService : IEmailService
{
    private readonly EmailClient? _emailClient;
    private readonly string? _senderAddress;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IHostEnvironment env)
    {
        _logger = logger;
        var connectionString = configuration["CommunicationServices:ConnectionString"];
        _senderAddress = configuration["CommunicationServices:SenderEmail"];

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(_senderAddress))
        {
            _logger.LogError("CommunicationServices:ConnectionString or SenderEmail is not set");
            if (env.IsProduction()) {
                throw new InvalidOperationException("Email service is not properly configured. Please check CommunicationServices settings in configuration.");
            }
        } else {
            _emailClient = new EmailClient(connectionString);
        }
    }

    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        try
        {
            var emailContent = new EmailContent(subject)
            {
                PlainText = !isHtml ? body : null,
                Html = isHtml ? body : null
            };

            var emailMessage = new EmailMessage(
                senderAddress: _senderAddress,
                recipientAddress: to,
                content: emailContent
            );

            if (_emailClient == null) {
                throw new InvalidOperationException("Email service is not properly configured. Please check CommunicationServices settings in configuration.");
            }

            var response = await _emailClient.SendAsync(
                wait: WaitUntil.Completed,
                message: emailMessage);

            if (response.Value.Status != EmailSendStatus.Succeeded)
            {
                throw new Exception($"Failed to send email: {response.Value.Status}");
            }
        }
        catch (Exception ex)
        {
            // Log the exception or handle it according to your needs
            throw new Exception("Error sending email", ex);
        }
    }
}

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body, bool isHtml = false);
}
