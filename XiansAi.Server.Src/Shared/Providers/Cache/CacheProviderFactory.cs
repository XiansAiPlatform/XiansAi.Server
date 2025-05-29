using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace XiansAi.Server.Providers;

/// <summary>
/// Implementation of cache provider factory
/// </summary>
public class CacheProviderFactory
{

    /// <summary>
    /// Registers the cache provider based on configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProvider(IServiceCollection services, IConfiguration configuration)
    {
        var cacheProvider = configuration["Cache:Provider"];
        if (string.IsNullOrWhiteSpace(cacheProvider))
        {
            throw new InvalidOperationException("Cache:Provider is not configured in appsettings");
        }

        // Register the appropriate provider based on configuration
        switch (cacheProvider.ToLowerInvariant())
        {
            case "redis":
                var connectionString = configuration["Cache:Redis:ConnectionString"];
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException("Redis cache provider requires Cache:Redis:ConnectionString");
                }
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = connectionString;
                });
                services.AddScoped<ICacheProvider, RedisCacheProvider>();
                break;
            case "memory":
                services.AddMemoryCache();
                services.AddScoped<ICacheProvider, InMemoryCacheProvider>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported cache provider: {cacheProvider}");
        }
    }

} 