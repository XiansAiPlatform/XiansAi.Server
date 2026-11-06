using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Caching.Memory;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Caches tenant lookups by tenant ID to reduce database load for frequently accessed tenants.
/// Used primarily by Admin API auth when SysAdmin specifies a target tenant.
/// Registered as Singleton to match IMemoryCache lifetime; uses IServiceScopeFactory to resolve
/// scoped ITenantRepository per operation.
/// </summary>
public interface ITenantCacheService
{
    /// <summary>
    /// Gets a tenant by ID from cache or database.
    /// A missing tenant is never cached: with the memory cache provider each pod has its own cache
    /// and a cached "not found" on one pod would keep failing requests for a tenant created on
    /// another pod until the entry expired. A lookup that finds no tenant also removes any stale
    /// entry for that id.
    /// Returns a shallow copy so callers cannot mutate the cached original.
    /// When <paramref name="bypassCache"/> is true, the cache is not read; the tenant is fetched
    /// directly from the database and the cache entry is refreshed with the fresh value.
    /// </summary>
    Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false);

    /// <summary>
    /// Invalidates the cached tenant when it is updated or deleted.
    /// </summary>
    void InvalidateTenant(string tenantId);
}

public class TenantCacheService : ITenantCacheService
{
    private const string CacheKeyPrefix = "tenant:byid:";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantCacheService> _logger;
    private readonly ICacheInvalidationBus _invalidationBus;
    private readonly ICacheOperationMode _cacheMode;
    private readonly TimeSpan _tenantCacheExpiration;

    public TenantCacheService(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TenantCacheService> logger,
        ICacheInvalidationBus invalidationBus,
        ICacheOperationMode cacheMode)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _invalidationBus = invalidationBus ?? throw new ArgumentNullException(nameof(invalidationBus));
        _cacheMode = cacheMode ?? throw new ArgumentNullException(nameof(cacheMode));

        var expirationMinutes = configuration.GetValue<int>("TenantCache:ExpirationMinutes", 5);
        _tenantCacheExpiration = TimeSpan.FromMinutes(Math.Max(1, expirationMinutes));
    }

    public async Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Validate format to prevent _keyLocks growth from arbitrary user-supplied IDs (enumeration attacks).
        tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);

        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        // Nothing is cached in this mode, so there is no shared state to protect — skip the
        // per-key lock entirely instead of serializing concurrent lookups for no benefit.
        if (_cacheMode.IsNoOp)
        {
            _logger.LogDebug("Fetching tenant {TenantId} from database (no-op cache provider)", tenantId);
            return (await FetchFromDatabaseAsync(tenantId, cancellationToken))?.ShallowCopy();
        }

        var semaphore = _keyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        var acquired = false;
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            acquired = true;

            // Invalidated on tenant updates, so skip caching when Cache:Provider=noop.
            if (!bypassCache && _cache.TryGetValue(cacheKey, out Tenant? cachedTenant) && cachedTenant != null)
                return cachedTenant.ShallowCopy();

            _logger.LogDebug("Fetching tenant {TenantId} from database (bypassCache: {BypassCache})", tenantId, bypassCache);

            var tenant = await FetchFromDatabaseAsync(tenantId, cancellationToken);

            if (tenant == null)
            {
                // Never cache a miss (see the interface doc). Drop any entry left over from before
                // the tenant was deleted so the next lookup does not serve stale data.
                _cache.Remove(cacheKey);
                _logger.LogDebug("Tenant {TenantId} not found; removed any cached entry", tenantId);
                return null;
            }

            // Do not remove semaphores in post-eviction callback: any check-then-remove is racy
            // and can break per-key mutual exclusion. Bounded growth (one entry per distinct
            // tenant ID) is acceptable since the number of tenants is typically small.
            var entryOptions = new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetAbsoluteExpiration(_tenantCacheExpiration);
            _cache.Set(cacheKey, tenant, entryOptions);

            _logger.LogDebug("Cached tenant {TenantId}", tenantId);
            return tenant.ShallowCopy();
        }
        finally
        {
            if (acquired) semaphore.Release();
        }
    }

    private async Task<Tenant?> FetchFromDatabaseAsync(string tenantId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        return await repo.GetByTenantIdAsync(tenantId, cancellationToken);
    }

    public void InvalidateTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("InvalidateTenant called with null/empty tenantId; skipping.");
            return;
        }
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "InvalidateTenant: invalid tenantId '{TenantId}'; skipping.", tenantId);
            return;
        }
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";
        _cache.Remove(cacheKey);
        _ = _invalidationBus.PublishAsync(new CacheInvalidationEnvelope(
            CacheInvalidationType.Tenant,
            UserId: null,
            tenantId,
            Keys: [cacheKey],
            DateTimeOffset.UtcNow));
        _logger.LogDebug("Invalidated tenant cache for {TenantId}", tenantId);
    }
}
