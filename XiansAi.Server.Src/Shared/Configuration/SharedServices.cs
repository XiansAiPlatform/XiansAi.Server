using Shared.Auth;
using XiansAi.Server.Utils;
using XiansAi.Server.Providers;
using Shared.Utils;
using Shared.Utils.GenAi;
using Shared.Utils.Temporal;
using Shared.Data;
using Shared.Services;

namespace Features.Shared.Configuration;

public static class SharedServices
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
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

        services.AddSingleton<IMongoDbContext>(sp =>
            new MongoDbContext(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILogger<MongoDbContext>>()));
        
        // Register MongoDB client
        services.AddScoped<IMongoDbClientService>(sp =>
            new MongoDbClientService(
                sp.GetRequiredService<IMongoDbContext>()));
        
        // Register database service
        services.AddScoped<IDatabaseService, DatabaseService>();
        
        // Register Temporal client
        services.AddScoped<ITemporalClientService>(sp =>
            new TemporalClientService(
                sp.GetRequiredService<ILogger<TemporalClientService>>(),
                sp.GetRequiredService<ITenantContext>()));
                
        // Register cache service
        services.AddScoped<ObjectCache>();
        
        // Add this to the AddInfrastructureServices method in SharedServices.cs
        services.AddSingleton<IBackgroundTaskService, BackgroundTaskService>();
        services.AddHostedService(sp => (BackgroundTaskService)sp.GetRequiredService<IBackgroundTaskService>());
        
        return services;
    }

    /// <summary>
    /// Registers cache services using the combined factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterCacheProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Register all available cache providers
        CacheProviderFactory.RegisterProviders(services, configuration);

        // Register the cache provider factory
        services.AddSingleton<ICacheProviderFactory, CacheProviderFactory>();
    }

    /// <summary>
    /// Registers email services using the combined factory
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterEmailProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Register all available email providers
        EmailProviderFactory.RegisterProviders(services, configuration);

        // Register the email provider factory
        services.AddSingleton<IEmailProviderFactory, EmailProviderFactory>();
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