namespace Shared.Providers;

/// <summary>
/// Cache provider that never actually caches: reads always miss, writes are ignored. Callers fall
/// through to their real source (e.g. the database) every time. Used when Cache:Provider=noop.
/// </summary>
public class NoOpCacheProvider : ICacheProvider
{
    public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);

    public Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null) =>
        Task.FromResult(false);

    public Task<bool> RemoveAsync(string key) => Task.FromResult(true);
}
