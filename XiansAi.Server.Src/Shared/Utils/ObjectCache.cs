using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace XiansAi.Server.Utils;

/// <summary>
/// Service for handling cached objects with customizable expiration policies
/// </summary>
public class ObjectCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ObjectCache> _logger;
    private readonly DistributedCacheEntryOptions _defaultCacheOptions;

    /// <summary>
    /// Creates a new instance of the ObjectCacheService
    /// </summary>
    /// <param name="cache">The distributed cache implementation</param>
    /// <param name="logger">Logger for the service</param>
    public ObjectCache(
        IDistributedCache cache,
        ILogger<ObjectCache> logger)
    {
        _cache = cache;
        _logger = logger;
        _defaultCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        };
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
            _logger.LogError(ex, "Error retrieving value from cache for key: {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Sets a value in cache with optional expiration settings
    /// </summary>
    /// <typeparam name="T">The type of the value to cache</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="relativeExpiration">Optional. The absolute expiration time relative to now. Default is 24 hours if no expiration is specified.</param>
    /// <param name="slidingExpiration">Optional. The sliding expiration time. No default value.</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? relativeExpiration = null, TimeSpan? slidingExpiration = null)
    {
        try
        {
            var cacheOptions = new DistributedCacheEntryOptions();
            
            if (relativeExpiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = relativeExpiration.Value;
            }
            else if (!slidingExpiration.HasValue)
            {
                // If neither option is provided, use the default relative expiration
                cacheOptions.AbsoluteExpirationRelativeToNow = _defaultCacheOptions.AbsoluteExpirationRelativeToNow;
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
            _logger.LogError(ex, "Error setting value in cache for key: {Key}", key);
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
            _logger.LogError(ex, "Error removing value from cache for key: {Key}", key);
            return false;
        }
    }
} 