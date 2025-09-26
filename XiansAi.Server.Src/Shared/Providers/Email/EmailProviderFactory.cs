using Azure.Communication.Email;

namespace Shared.Providers;


/// <summary>
/// Implementation of email provider factory
/// </summary>
public class EmailProviderFactory 
{
    /// <summary>
    /// Registers the email provider based on configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProvider(IServiceCollection services, IConfiguration configuration)
    {
        var emailProvider = configuration["Email:Provider"];
        if (string.IsNullOrWhiteSpace(emailProvider))
        {
            // Default to console email provider if not configured
            services.AddScoped<IEmailProvider, ConsoleEmailProvider>();
            return;
        }
        // Register the appropriate provider based on configuration
        switch (emailProvider.ToLowerInvariant())
        {
            case "azure":
                var connectionString = configuration["Email:Azure:ConnectionString"];
                var senderEmail = configuration["Email:Azure:SenderEmail"];
                if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(senderEmail))
                {
                    throw new InvalidOperationException("Azure email provider requires Email:Azure:ConnectionString and Email:Azure:SenderEmail");
                }
                services.AddScoped<IEmailProvider, AzureCommunicationEmailProvider>(sp =>
                {
                    var emailClient = new EmailClient(connectionString);
                    var logger = sp.GetRequiredService<ILogger<AzureCommunicationEmailProvider>>();
                    return new AzureCommunicationEmailProvider(emailClient, senderEmail, logger);
                });
                break;
            case "console":
                services.AddScoped<IEmailProvider, ConsoleEmailProvider>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported email provider: {emailProvider}");
        }
    }

} 