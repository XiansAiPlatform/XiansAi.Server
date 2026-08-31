using Microsoft.Extensions.Caching.Memory;
using Shared.Services;

namespace Shared.Providers;

/// <summary>
/// Applies a cache invalidation envelope to caches local to this server instance.
/// </summary>
public sealed class CacheInvalidationApplicator : ICacheInvalidationApplicator
{
    private readonly IUserCacheIndex _userCacheIndex;
    private readonly IMemoryCache _memoryCache;
    private readonly IIncomingOriginCache _incomingOriginCache;
    private readonly ILogger<CacheInvalidationApplicator> _logger;

    public CacheInvalidationApplicator(
        IUserCacheIndex userCacheIndex,
        IMemoryCache memoryCache,
        IIncomingOriginCache incomingOriginCache,
        ILogger<CacheInvalidationApplicator> logger)
    {
        _userCacheIndex = userCacheIndex ?? throw new ArgumentNullException(nameof(userCacheIndex));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _incomingOriginCache = incomingOriginCache ?? throw new ArgumentNullException(nameof(incomingOriginCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Apply(CacheInvalidationEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.Type)
        {
            case CacheInvalidationType.UserAuth:
                if (!string.IsNullOrWhiteSpace(envelope.UserId))
                {
                    _userCacheIndex.Invalidate(envelope.UserId);
                }
                break;

            case CacheInvalidationType.Tenant:
                if (envelope.Keys is { Count: > 0 })
                {
                    RemoveKeys(envelope.Keys);
                }
                else if (!string.IsNullOrWhiteSpace(envelope.TenantId))
                {
                    _memoryCache.Remove($"tenant:byid:{envelope.TenantId}");
                }
                break;

            case CacheInvalidationType.ApiKey:
            case CacheInvalidationType.Activation:
            case CacheInvalidationType.AgentWorkflowTypes:
            case CacheInvalidationType.ThreadId:
                RemoveKeys(envelope.Keys);
                break;

            case CacheInvalidationType.ThreadOrigin:
                // V1 convention: the first key carries the globally unique thread id.
                var threadId = envelope.Keys?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(threadId))
                {
                    // Applying a received event must not publish it again.
                    _incomingOriginCache.InvalidateThread(threadId, publish: false);
                }
                break;

            default:
                _logger.LogWarning(
                    "Ignoring unsupported cache invalidation type {Type}",
                    envelope.Type);
                break;
        }
    }

    private void RemoveKeys(IReadOnlyList<string>? keys)
    {
        if (keys is null)
        {
            return;
        }

        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _memoryCache.Remove(key);
            }
        }
    }
}
