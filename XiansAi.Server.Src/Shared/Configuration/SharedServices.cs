using Shared.Auth;
using XiansAi.Server.Utils;
using XiansAi.Server.Providers;
using Shared.Utils;
using Shared.Utils.GenAi;
using Shared.Utils.Temporal;
using Shared.Data;

namespace Features.Shared.Configuration;

public static class SharedServices
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register cache services using the combined factory
        RegisterCacheServices(services, configuration);

        // Register core services
        services.AddSingleton<CertificateGenerator>();
        
        // Register business services
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IEmailService, EmailService>();

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
                
        // Register OpenAI client
        services.AddScoped<IOpenAIClientService, OpenAIClientService>(sp =>
            new OpenAIClientService(
                sp.GetRequiredService<ITenantContext>().GetOpenAIConfig(),
                sp.GetRequiredService<ILogger<OpenAIClientService>>(),
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
    private static void RegisterCacheServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register all available cache providers
        CacheProviderFactory.RegisterProviders(services, configuration);

        // Register the cache provider factory
        services.AddSingleton<ICacheProviderFactory, CacheProviderFactory>();
    }
} 