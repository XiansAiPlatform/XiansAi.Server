using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace XiansAi.Server.Providers;

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
            throw new InvalidOperationException("Cache:Provider or Cache__Provider is not configured in appsettings or environment variables");
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