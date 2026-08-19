using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Records which cache entries were written on behalf of which user, so that a change to an
/// account can evict all of them at once.
///
/// The authorization caches are keyed on what the request presented — a hash of a token, a
/// certificate thumbprint, a provider authority and subject — and none of those can be
/// reconstructed from a user id. Without this index, disabling an account would leave every
/// cached decision about it in place until it expired on its own, which is how long the account
/// would keep working after being disabled.
/// </summary>
public interface IUserCacheIndex
{
    /// <summary>Associates a cache key with the user whose authorization it depends on.</summary>
    void Track(string userId, string cacheKey);

    /// <summary>
    /// Drops the association, for entries the cache evicted on its own. Callers register this as a
    /// post-eviction callback so the index does not grow without bound.
    /// </summary>
    void Forget(string userId, string cacheKey);

    /// <summary>Evicts every entry tracked for the user. Returns how many were removed.</summary>
    int Invalidate(string userId);
}

/// <summary>
/// Must be registered as a singleton. The caches that populate it are scoped, so an index held by
/// one of them would be discarded at the end of the request that wrote the entry and would be
/// empty in the later request that needs to evict it.
/// </summary>
public class UserCacheIndex : IUserCacheIndex
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserCacheIndex> _logger;

    // Compared without regard to case, because the same account reaches this under spellings that
    // differ in case — an address is stored lowercase while a token may present it otherwise.
    // Treating two spellings as one user can only evict more than was needed, which costs a cache
    // miss, whereas treating them as two would leave an entry behind.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByUser =
        new(StringComparer.OrdinalIgnoreCase);

    public UserCacheIndex(IMemoryCache cache, ILogger<UserCacheIndex> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Track(string userId, string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(cacheKey))
        {
            return;
        }

        var keys = _keysByUser.GetOrAdd(
            userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        keys[cacheKey] = 0;
    }

    public void Forget(string userId, string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(cacheKey))
        {
            return;
        }

        if (!_keysByUser.TryGetValue(userId, out var keys))
        {
            return;
        }

        keys.TryRemove(cacheKey, out _);

        // A racing Track may have just added a key to this instance, so the removal is conditional
        // on it still being empty. Losing that race only leaves an empty entry behind.
        if (keys.IsEmpty)
        {
            _keysByUser.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, byte>>(userId, keys));
        }
    }

    public int Invalidate(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return 0;
        }

        if (!_keysByUser.TryRemove(userId, out var keys))
        {
            return 0;
        }

        var cacheKeys = keys.Keys.ToArray();
        foreach (var cacheKey in cacheKeys)
        {
            _cache.Remove(cacheKey);
        }

        if (cacheKeys.Length > 0)
        {
            _logger.LogInformation(
                "Evicted {Count} cached authorization entries for {UserId}",
                cacheKeys.Length, LogSanitizer.RedactUserId(userId));
        }

        return cacheKeys.Length;
    }
}
