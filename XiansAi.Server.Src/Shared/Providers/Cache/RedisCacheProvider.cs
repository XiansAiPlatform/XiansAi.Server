using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Shared.Providers;

/// <summary>
/// Redis implementation of the cache provider using IDistributedCache
/// </summary>
public class RedisCacheProvider : ICacheProvider
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheProvider> _logger;

    /// <summary>
    /// Creates a new instance of the RedisCacheProvider
    /// </summary>
    /// <param name="cache">The distributed cache implementation</param>
    /// <param name="logger">Logger for the provider</param>
    public RedisCacheProvider(
        IDistributedCache cache,
        ILogger<RedisCacheProvider> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a value from cache by key
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The cache key</param>
    /// <returns>The cached value or default if not found</returns>
    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _cache.GetStringAsync(key);
            if (string.IsNullOrEmpty(value))
                return default;

            return JsonSerializer.Deserialize<T>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from Redis cache for key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Sets a value in cache with optional expiration settings
    /// </summary>
    /// <typeparam name="T">The type of the value to cache</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="absoluteExpiration">Optional. The absolute expiration time relative to now</param>
    /// <param name="slidingExpiration">Optional. The sliding expiration time</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null)
    {
        try
        {
            var cacheOptions = new DistributedCacheEntryOptions();
            
            if (absoluteExpiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = absoluteExpiration.Value;
            }
            
            if (slidingExpiration.HasValue)
            {
                cacheOptions.SlidingExpiration = slidingExpiration.Value;
            }
            
            var serializedValue = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, serializedValue, cacheOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in Redis cache for key: {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Removes a value from cache by key
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    public async Task<bool> RemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing value from Redis cache for key: {Key}", key);
            return false;
        }
    }
} 