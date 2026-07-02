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
        // Bind usage events options
        services.Configure<UsageEventsOptions>(configuration.GetSection(UsageEventsOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<UsageEventsOptions>>().Value);

        // Bind outbound webhook options and register the delivery infrastructure
        RegisterWebhookServices(services, configuration);

        // Register cache services using the combined factory
        RegisterCacheProviders(services, configuration);

        // Register email services using the combined factory
        RegisterEmailProviders(services, configuration);

        // Register LLM services using the combined factory
        RegisterLlmProviders(services, configuration);

        // Register the active Secret Store provider (database default, optional Azure Key Vault)
        RegisterSecretStoreProvider(services, configuration);

        // Register core services
        services.AddSingleton<CertificateGenerator>();
        
        // Register secrets encryption service (singleton for performance)
        services.AddSingleton<ISecretsEncryptionService, SecretsEncryptionService>();
        
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
        
        // Register admin task service (shared between AdminApi and WebApi)
        services.AddScoped<IAdminTaskService, AdminTaskService>();
        
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
    /// Registers the outbound webhook options, the named HttpClient used for delivery, and the
    /// background dispatcher. The dispatcher is only registered when webhooks are enabled; when
    /// several instances run, its atomic outbox claim guarantees each event is delivered once.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterWebhookServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WebhooksOptions>(configuration.GetSection(WebhooksOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<WebhooksOptions>>().Value);

        var webhookOptions = configuration.GetSection(WebhooksOptions.SectionName).Get<WebhooksOptions>()
            ?? new WebhooksOptions();

        var requestTimeout = TimeSpan.FromSeconds(Math.Max(1, webhookOptions.RequestTimeoutSeconds));
        services.AddHttpClient("Webhooks", client =>
        {
            client.Timeout = requestTimeout;
            // Cap buffering in case any code path reads the response body; deliveries never do.
            client.MaxResponseContentBufferSize = 64 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XiansAi-Webhooks/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            // Do not follow redirects: a compromised listener could 3xx-redirect the dispatcher
            // toward internal endpoints (SSRF). Redirects are surfaced as non-2xx failures instead.
            AllowAutoRedirect = false,
            // Webhook delivery is stateless; never send or store cookies.
            UseCookies = false,
            // Avoid decompressing attacker-controlled response bodies.
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = requestTimeout
        });

        if (webhookOptions.Enabled)
        {
            services.AddHostedService<WebhookDispatcherService>();
        }
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

    /// <summary>
    /// Registers the active Secret Store provider (used by SecretVaultService) based on configuration.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void RegisterSecretStoreProvider(IServiceCollection services, IConfiguration configuration)
    {
        SecretStoreProviderFactory.RegisterProvider(services, configuration);
    }
} 