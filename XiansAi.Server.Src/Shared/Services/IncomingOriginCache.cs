using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Shared.Services;

/// <summary>
/// Origin and platform metadata of the last incoming message of a conversation thread, per scope.
/// Outgoing replies use it to auto-populate routing information without querying MongoDB.
///
/// Deleting messages makes these entries wrong, so every delete path must invalidate them:
/// a reply built from a stale entry is routed to a channel the participant no longer has.
/// Registered as Singleton to match the IMemoryCache lifetime.
/// </summary>
public interface IIncomingOriginCache
{
    /// <summary>
    /// Returns the cached value, or null when nothing is cached for this thread and scope.
    /// </summary>
    IncomingOriginData? Get(string tenantId, string threadId, string? scope);

    void Set(string tenantId, string threadId, string? scope, IncomingOriginData data);

    /// <summary>
    /// Drops every scope cached for the thread. Thread ids are globally unique, so no tenant id is needed.
    /// </summary>
    void InvalidateThread(string threadId);

    /// <summary>
    /// Drops a single scope of a thread, for deletes that only remove messages of one topic.
    /// </summary>
    void InvalidateScope(string tenantId, string threadId, string? scope);
}

/// <summary>
/// Origin and Data may both be null: that is a valid "the thread has no usable incoming message"
/// result, and caching it avoids repeating the lookup on every reply.
/// </summary>
public sealed record IncomingOriginData(string? Origin, object? Data);

public class IncomingOriginCache : IIncomingOriginCache
{
    private const string CacheKeyPrefix = "msg:last-incoming-origin:";
    private const string DefaultScopeKey = "__default__";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// One cancellation source per thread, attached as an expiration token to every entry of that
    /// thread so a single cancel evicts all of its scopes without having to enumerate them.
    /// Held outside IMemoryCache on purpose: as a cache entry it could be evicted by the cache size
    /// limit while the entries it is supposed to invalidate are still alive.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _threadEvictionSources = new();

    private readonly IMemoryCache _cache;
    private readonly ILogger<IncomingOriginCache> _logger;

    public IncomingOriginCache(IMemoryCache cache, ILogger<IncomingOriginCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IncomingOriginData? Get(string tenantId, string threadId, string? scope)
    {
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(threadId))
        {
            return null;
        }

        return _cache.TryGetValue(BuildKey(tenantId, threadId, scope), out IncomingOriginData? cached)
            ? cached
            : null;
    }

    public void Set(string tenantId, string threadId, string? scope, IncomingOriginData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(threadId))
        {
            _logger.LogDebug("Skipping incoming origin cache write: tenant id or thread id missing");
            return;
        }

        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheDuration)
            .SetSize(1)
            .AddExpirationToken(new CancellationChangeToken(GetOrCreateEvictionSource(threadId).Token));

        _cache.Set(BuildKey(tenantId, threadId, scope), data, options);
    }

    public void InvalidateThread(string threadId)
    {
        if (string.IsNullOrEmpty(threadId))
        {
            return;
        }

        if (_threadEvictionSources.TryRemove(threadId, out var evictionSource))
        {
            evictionSource.Cancel();
        }
    }

    public void InvalidateScope(string tenantId, string threadId, string? scope)
    {
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(threadId))
        {
            return;
        }

        _cache.Remove(BuildKey(tenantId, threadId, scope));
    }

    private static string BuildKey(string tenantId, string threadId, string? scope)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? DefaultScopeKey : scope.Trim();
        return $"{CacheKeyPrefix}{tenantId}:{threadId}:{normalizedScope}";
    }

    private CancellationTokenSource GetOrCreateEvictionSource(string threadId)
    {
        while (true)
        {
            var evictionSource = _threadEvictionSources.GetOrAdd(threadId, CreateEvictionSource);
            if (!evictionSource.IsCancellationRequested)
            {
                return evictionSource;
            }

            // Cancelled by a concurrent invalidation: it can no longer guard new entries, so replace it.
            // An entry that was already being written against the cancelled token is dropped by
            // MemoryCache on insert, so it cannot survive the invalidation that just happened.
            _threadEvictionSources.TryRemove(new KeyValuePair<string, CancellationTokenSource>(threadId, evictionSource));
        }
    }

    private CancellationTokenSource CreateEvictionSource(string threadId)
    {
        // Self-cancels after the cache duration so idle threads do not accumulate here forever.
        // Entries of a still-active thread are simply re-cached against a fresh source.
        var evictionSource = new CancellationTokenSource(CacheDuration);
        evictionSource.Token.Register(
            () => _threadEvictionSources.TryRemove(
                new KeyValuePair<string, CancellationTokenSource>(threadId, evictionSource)));
        return evictionSource;
    }
}
