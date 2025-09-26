namespace Shared.Providers;

/// <summary>
/// Interface for cache providers that abstracts caching implementation details
/// </summary>
public interface ICacheProvider
{
    /// <summary>
    /// Retrieves a value from cache by key
    /// </summary>
    /// <typeparam name="T">The type of the cached value</typeparam>
    /// <param name="key">The cache key</param>
    /// <returns>The cached value or default if not found</returns>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Sets a value in cache with optional expiration settings
    /// </summary>
    /// <typeparam name="T">The type of the value to cache</typeparam>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="absoluteExpiration">Optional. The absolute expiration time relative to now</param>
    /// <param name="slidingExpiration">Optional. The sliding expiration time</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null);

    /// <summary>
    /// Removes a value from cache by key
    /// </summary>
    /// <param name="key">The cache key to remove</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    Task<bool> RemoveAsync(string key);
} 