using Features.Shared.Auth;
using XiansAi.Server.Auth;
using XiansAi.Server.Database;
using XiansAi.Server.GenAi;
using XiansAi.Server.Temporal;
using XiansAi.Server.Utils;

namespace Features.Shared.Configuration;

public static class SharedServices
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register Redis cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetRequiredSection("RedisCache:ConnectionString").Value;
            options.InstanceName = "XiansAi:";
        });

        // Register core services
        services.AddSingleton<CertificateGenerator>();
        
        // Register business services
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IEmailService, EmailService>();
        
        // Register MongoDB client
        services.AddScoped<IMongoDbClientService>(sp =>
            new MongoDbClientService(
                sp.GetRequiredService<ITenantContext>().GetMongoDBConfig(),
                sp.GetRequiredService<ITenantContext>()));
        
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
        services.AddScoped<ObjectCacheService>();
        
        // Add this to the AddInfrastructureServices method in SharedServices.cs
        services.AddSingleton<IBackgroundTaskService, BackgroundTaskService>();
        services.AddHostedService(sp => (BackgroundTaskService)sp.GetRequiredService<IBackgroundTaskService>());
        
        return services;
    }
} 