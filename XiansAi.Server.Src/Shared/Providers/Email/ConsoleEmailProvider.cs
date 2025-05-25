namespace XiansAi.Server.Providers;

/// <summary>
/// Console implementation of the email provider that prints emails to console
/// </summary>
public class ConsoleEmailProvider : IEmailProvider, IEmailProviderRegistration
{
    private readonly ILogger<ConsoleEmailProvider> _logger;

    /// <summary>
    /// Gets the name of this provider
    /// </summary>
    public static string ProviderName => "Console";

    /// <summary>
    /// Gets the priority of this provider (lower numbers = higher priority)
    /// </summary>
    public static int Priority => 100; // Lower priority than Azure Communication Services

    /// <summary>
    /// Creates a new instance of the ConsoleEmailProvider
    /// </summary>
    /// <param name="logger">Logger for the provider</param>
    public ConsoleEmailProvider(ILogger<ConsoleEmailProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Determines if this provider can be registered with the given configuration
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>True if the provider can be registered, false otherwise</returns>
    public static bool CanRegister(IConfiguration configuration)
    {
        // Console provider can always be registered as it has no external dependencies
        return true;
    }

    /// <summary>
    /// Registers the services required by this provider
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // No additional services needed for console provider
    }

    /// <summary>
    /// Sends an email message
    /// </summary>
    /// <param name="to">The recipient email address</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully, false otherwise</returns>
    public Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false)
    {
        return SendEmailAsync(new[] { to }, subject, body, isHtml);
    }

    /// <summary>
    /// Sends an email message to multiple recipients
    /// </summary>
    /// <param name="to">The list of recipient email addresses</param>
    /// <param name="subject">The email subject</param>
    /// <param name="body">The email body content</param>
    /// <param name="isHtml">Whether the body content is HTML formatted</param>
    /// <returns>True if the email was sent successfully to all recipients, false otherwise</returns>
    public Task<bool> SendEmailAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        try
        {
            var recipients = string.Join(", ", to);
            var contentType = isHtml ? "HTML" : "Plain Text";
            
            var emailOutput = $"""
                ===============================================
                📧 EMAIL SENT TO CONSOLE
                ===============================================
                To: {recipients}
                Subject: {subject}
                Content Type: {contentType}
                -----------------------------------------------
                {body}
                ===============================================
                """;

            Console.WriteLine(emailOutput);
            _logger.LogInformation("Console email sent to {RecipientCount} recipients: {Recipients}", 
                to.Count(), recipients);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending console email to {RecipientCount} recipients", to.Count());
            return Task.FromResult(false);
        }
    }
} 