using Azure.Communication.Email;

namespace XiansAi.Server.Providers;

/// <summary>
/// Factory for creating email providers based on configuration
/// </summary>
public interface IEmailProviderFactory
{
    /// <summary>
    /// Creates an email provider based on the current configuration
    /// </summary>
    /// <returns>The appropriate email provider implementation</returns>
    IEmailProvider CreateEmailProvider();
}

/// <summary>
/// Provider definition for email providers
/// </summary>
public class EmailProviderDefinition
{
    public required string Name { get; init; }
    public required int Priority { get; init; }
    public required Func<IConfiguration, bool> CanRegister { get; init; }
    public required Action<IServiceCollection, IConfiguration> RegisterServices { get; init; }
    public required Func<IServiceProvider, IEmailProvider?> CreateProvider { get; init; }
}

/// <summary>
/// Implementation of email provider factory that also handles service registration
/// </summary>
public class EmailProviderFactory : IEmailProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailProviderFactory> _logger;

    /// <summary>
    /// All known email providers in a single definition
    /// </summary>
    private static readonly EmailProviderDefinition[] Providers = new[]
    {
        new EmailProviderDefinition
        {
            Name = AzureCommunicationEmailProvider.ProviderName,
            Priority = AzureCommunicationEmailProvider.Priority,
            CanRegister = AzureCommunicationEmailProvider.CanRegister,
            RegisterServices = AzureCommunicationEmailProvider.RegisterServices,
            CreateProvider = serviceProvider =>
            {
                var emailClient = serviceProvider.GetService<EmailClient>();
                var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                var senderAddress = configuration["CommunicationServices:SenderEmail"];
                
                if (emailClient != null && !string.IsNullOrEmpty(senderAddress))
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<AzureCommunicationEmailProvider>>();
                    return new AzureCommunicationEmailProvider(emailClient, senderAddress, logger);
                }
                return null;
            }
        },
        new EmailProviderDefinition
        {
            Name = ConsoleEmailProvider.ProviderName,
            Priority = ConsoleEmailProvider.Priority,
            CanRegister = ConsoleEmailProvider.CanRegister,
            RegisterServices = ConsoleEmailProvider.RegisterServices,
            CreateProvider = serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILogger<ConsoleEmailProvider>>();
                return new ConsoleEmailProvider(logger);
            }
        }
    };

    /// <summary>
    /// Creates a new instance of the EmailProviderFactory
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="logger">Logger for the factory</param>
    public EmailProviderFactory(
        IServiceProvider serviceProvider,
        ILogger<EmailProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers all available email providers in priority order
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Sort by priority (lower numbers = higher priority) and register
        foreach (var provider in Providers.OrderBy(p => p.Priority))
        {
            if (provider.CanRegister(configuration))
            {
                try
                {
                    provider.RegisterServices(services, configuration);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to register email provider: {provider.Name} - {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Creates an email provider based on priority order
    /// </summary>
    /// <returns>The appropriate email provider implementation</returns>
    public IEmailProvider CreateEmailProvider()
    {
        // Try providers in priority order (lower numbers = higher priority)
        foreach (var provider in Providers.OrderBy(p => p.Priority))
        {
            try
            {
                var instance = provider.CreateProvider(_serviceProvider);
                if (instance != null)
                {
                    _logger.LogInformation("Successfully created email provider: {ProviderName} (Priority: {Priority})", 
                        provider.Name, provider.Priority);
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create email provider: {ProviderName}. Trying next provider.", provider.Name);
            }
        }

        throw new InvalidOperationException("No email providers could be created. Ensure at least one email provider is properly registered.");
    }
} 