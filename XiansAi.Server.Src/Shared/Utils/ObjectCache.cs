using XiansAi.Server.Providers;

namespace XiansAi.Server.Utils;

/// <summary>
/// Service for handling cached objects with customizable expiration policies
/// </summary>
public class ObjectCache
{
    private readonly ICacheProvider _cacheProvider;
    private readonly ILogger<ObjectCache> _logger;
    private readonly TimeSpan _defaultExpiration;

    /// <summary>
    /// Creates a new instance of the ObjectCacheService
    /// </summary>
    /// <param name="cacheProvider">Cache provider</param>
    /// <param name="logger">Logger for the service</param>
    public ObjectCache(
        ICacheProvider cacheProvider,
        ILogger<ObjectCache> logger)
    {
        _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultExpiration = TimeSpan.FromHours(24);
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
            return await _cacheProvider.GetAsync<T>(key);
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
            var absoluteExpiration = relativeExpiration ?? (slidingExpiration.HasValue ? null : _defaultExpiration);
            return await _cacheProvider.SetAsync(key, value, absoluteExpiration, slidingExpiration);
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
            return await _cacheProvider.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing value from cache for key: {Key}", key);
            return false;
        }
    }
} 