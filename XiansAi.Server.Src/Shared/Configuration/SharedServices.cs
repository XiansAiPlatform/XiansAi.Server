using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Utils;
using Shared.Providers;
using Shared.Utils.Temporal;
using Shared.Data;
using Shared.Services;
using Shared.Configuration;

namespace Features.Shared.Configuration;

public static class SharedServices
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind token usage options
        services.Configure<TokenUsageOptions>(configuration.GetSection(TokenUsageOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<TokenUsageOptions>>().Value);

        // Register cache services using the combined factory
        RegisterCacheProviders(services, configuration);

        // Register email services using the combined factory
        RegisterEmailProviders(services, configuration);

        // Register LLM services using the combined factory
        RegisterLlmProviders(services, configuration);

        // Register core services
        services.AddSingleton<CertificateGenerator>();
        
        // Register business services
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ILlmService, LlmService>();
        services.AddScoped<IAuthorizationCacheService, AuthorizationCacheService>();
        

        // Register MongoDB context as singleton - it only reads configuration
        services.AddSingleton<IMongoDbContext, MongoDbContext>();
        
        // Register MongoDB client as singleton - MongoClient is thread-safe and should be reused
        services.AddSingleton<IMongoDbClientService, MongoDbClientService>();
        
        // Register database service
        services.AddScoped<IDatabaseService, DatabaseService>();
        
        // Register MongoDB index synchronization
        services.AddScoped<IMongoIndexSynchronizer, MongoIndexSynchronizer>();
        
        // Register Temporal client as singleton for better connection management
        services.AddSingleton<ITemporalClientService, TemporalClientService>();
        
        // Register a factory service for tenant-aware temporal operations
        services.AddScoped<ITemporalClientFactory, TemporalClientFactory>();

        // Register tenant service
        services.AddScoped<ITenantService, TenantService>();

        
        // Register cache service
        services.AddScoped<ObjectCache>();
        
        // Add background task service only if not already registered (to support test mocks)
        if (!services.Any(s => s.ServiceType == typeof(IBackgroundTaskService)))
        {
            services.AddSingleton<BackgroundTaskService>();
            services.AddSingleton<IBackgroundTaskService>(sp => sp.GetRequiredService<BackgroundTaskService>());
            services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BackgroundTaskService>());
        }
        
        return services;
    }

    /// <summary>
    /// Registers cache services using the combined factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterCacheProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Register the active cache provider and the factory itself
        CacheProviderFactory.RegisterProvider(services, configuration);
    }

    /// <summary>
    /// Registers email services using the combined factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterEmailProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Register the active email provider and the factory itself
        EmailProviderFactory.RegisterProvider(services, configuration);
    }

    /// <summary>
    /// Registers LLM services using the combined factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterLlmProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Register the active LLM provider and the factory itself
        LlmProviderFactory.RegisterProvider(services, configuration);
    }
} 