using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;

public class EmailService : IEmailService
{
    private readonly EmailClient _emailClient;
    private readonly string? _senderAddress;

    public EmailService(IConfiguration configuration)
    {
        var connectionString = configuration["CommunicationServices:ConnectionString"];
        _senderAddress = configuration["CommunicationServices:SenderEmail"];
        _emailClient = new EmailClient(connectionString);
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
