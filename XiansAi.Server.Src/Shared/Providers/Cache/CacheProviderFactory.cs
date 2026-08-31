using StackExchange.Redis;
using Shared.Services;

namespace Shared.Providers;

/// <summary>
/// Implementation of cache provider factory
/// </summary>
public class CacheProviderFactory
{
    /// <summary>
    /// Gets configuration value supporting both colon and double underscore formats
    /// </summary>
    private static string? GetConfigValue(IConfiguration configuration, string key)
    {
        // Try colon format first (appsettings.json)
        var value = configuration[key];
        
        // If not found, try double underscore format (Azure environment variables)
        if (string.IsNullOrWhiteSpace(value))
        {
            value = configuration[key.Replace(":", "__")];
        }
        
        return value;
    }

    /// <summary>
    /// Registers the cache provider based on configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProvider(IServiceCollection services, IConfiguration configuration)
    {
        var cacheProvider = GetConfigValue(configuration, "Cache:Provider");
        if (string.IsNullOrWhiteSpace(cacheProvider))
        {
            // Default to memory cache if not configured.
            // IMemoryCache (with SizeLimit) is registered by SharedConfiguration before this factory runs.
            services.AddScoped<ICacheProvider, InMemoryCacheProvider>();
            services.AddSingleton<ICacheInvalidationBus, NoOpCacheInvalidationBus>();
            services.AddSingleton<IPendingRequestCoordinator, NoOpPendingRequestCoordinator>();
            return;
        }

        // Register the appropriate provider based on configuration
        switch (cacheProvider.ToLowerInvariant())
        {
            case "redis":
                var connectionString = GetConfigValue(configuration, "Cache:Redis:ConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Redis cache provider requires Cache:Redis:ConnectionString or Cache__Redis__ConnectionString");
                }
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = connectionString;
                });
                services.AddScoped<ICacheProvider, RedisCacheProvider>();
                services.AddSingleton<IConnectionMultiplexer>(_ =>
                    ConnectionMultiplexer.Connect(connectionString));
                services.AddSingleton<RedisCacheInvalidationBus>();
                services.AddSingleton<ICacheInvalidationBus>(serviceProvider =>
                    serviceProvider.GetRequiredService<RedisCacheInvalidationBus>());
                services.AddSingleton<IHostedService>(serviceProvider =>
                    serviceProvider.GetRequiredService<RedisCacheInvalidationBus>());
                services.AddSingleton<RedisPendingRequestCoordinator>();
                services.AddSingleton<IPendingRequestCoordinator>(serviceProvider =>
                    serviceProvider.GetRequiredService<RedisPendingRequestCoordinator>());
                services.AddSingleton<IHostedService>(serviceProvider =>
                    serviceProvider.GetRequiredService<RedisPendingRequestCoordinator>());
                break;
            case "memory":
                // IMemoryCache (with SizeLimit) is registered by SharedConfiguration before this factory runs.
                services.AddScoped<ICacheProvider, InMemoryCacheProvider>();
                services.AddSingleton<ICacheInvalidationBus, NoOpCacheInvalidationBus>();
                services.AddSingleton<IPendingRequestCoordinator, NoOpPendingRequestCoordinator>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported cache provider: {cacheProvider}");
        }
    }
} 