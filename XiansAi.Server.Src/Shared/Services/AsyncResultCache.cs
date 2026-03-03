using Microsoft.Extensions.Caching.Memory;

namespace Shared.Services;

/// <summary>
/// Generic async cache providing GetOrAdd pattern with configurable TTL.
/// Use for caching expensive async operations (e.g. validation, lookups) to reduce database load.
/// </summary>
public interface IAsyncResultCache
{
    /// <summary>
    /// Gets a value from cache or computes it via the factory and caches the result.
    /// </summary>
    /// <typeparam name="T">The type of the cached value. Must be a reference type.</typeparam>
    /// <param name="key">Unique cache key.</param>
    /// <param name="factory">Async factory to compute the value when not cached.</param>
    /// <param name="absoluteExpiration">How long the entry should be cached.</param>
    /// <param name="size">Relative size of the entry for eviction (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or freshly computed value.</returns>
    Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan absoluteExpiration,
        long size = 1,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes an entry from the cache.
    /// </summary>
    void Remove(string key);
}

public class AsyncResultCache : IAsyncResultCache
{
    private readonly IMemoryCache _cache;

    public AsyncResultCache(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<T> GetOrAddAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan absoluteExpiration,
        long size = 1,
        CancellationToken cancellationToken = default) where T : class
    {
        if (_cache.TryGetValue(key, out T? cached) && cached != null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(absoluteExpiration)
            .SetSize(size);
        _cache.Set(key, value, options);
        return value;
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
    }
}
