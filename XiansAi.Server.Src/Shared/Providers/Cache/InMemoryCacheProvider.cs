using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Shared.Providers;

/// <summary>
/// In-memory implementation of the cache provider using MemoryCache
/// </summary>
public class InMemoryCacheProvider : ICacheProvider
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryCacheProvider> _logger;
    private readonly ConcurrentDictionary<string, Timer> _timers;

    /// <summary>
    /// Creates a new instance of the InMemoryCacheProvider
    /// </summary>
    /// <param name="cache">The memory cache implementation</param>
    /// <param name="logger">Logger for the provider</param>
    public InMemoryCacheProvider(
        IMemoryCache cache,
        ILogger<InMemoryCacheProvider> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timers = new ConcurrentDictionary<string, Timer>();
    }

    /// <summary>
    /// Retrieves a value from cache by key
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The cache key</param>
    /// <returns>The cached value or default if not found</returns>
    public Task<T?> GetAsync<T>(string key)
    {
        try
        {
            if (_cache.TryGetValue(key, out var value))
            {
                if (value is string stringValue)
                {
                    var deserializedValue = JsonSerializer.Deserialize<T>(stringValue);
                    return Task.FromResult(deserializedValue);
                }
                
                if (value is T directValue)
                {
                    return Task.FromResult<T?>(directValue);
                }
            }

            return Task.FromResult<T?>(default);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from memory cache for key: {Key}", key);
            return Task.FromResult<T?>(default);
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
    public Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null)
    {
        try
        {
            var cacheOptions = new MemoryCacheEntryOptions();
            
            if (absoluteExpiration.HasValue)
            {
                cacheOptions.AbsoluteExpirationRelativeToNow = absoluteExpiration.Value;
            }
            
            if (slidingExpiration.HasValue)
            {
                cacheOptions.SlidingExpiration = slidingExpiration.Value;
            }

            // Register callback to clean up timers when item is removed
            cacheOptions.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
            {
                if (evictedKey is string keyString && _timers.TryRemove(keyString, out var timer))
                {
                    timer.Dispose();
                }
            });
            
            var serializedValue = JsonSerializer.Serialize(value);
            _cache.Set(key, serializedValue, cacheOptions);
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in memory cache for key: {Key}", key);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Removes a value from cache by key
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    public Task<bool> RemoveAsync(string key)
    {
        try
        {
            _cache.Remove(key);
            
            // Clean up any associated timer
            if (_timers.TryRemove(key, out var timer))
            {
                timer.Dispose();
            }
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing value from memory cache for key: {Key}", key);
            return Task.FromResult(false);
        }
    }
} 