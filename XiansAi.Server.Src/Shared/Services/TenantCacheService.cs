using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Data.Models;
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
    /// Null results are cached with a shorter TTL to mitigate brute-force enumeration.
    /// Returns a shallow copy so callers cannot mutate the cached original.
    /// </summary>
    Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached tenant when it is updated or deleted.
    /// </summary>
    void InvalidateTenant(string tenantId);
}

public class TenantCacheService : ITenantCacheService
{
    /// <summary>
    /// Non-null wrapper so we can distinguish "cached null" from "key not present".
    /// IMemoryCache.TryGetValue returns false for stored null because the out parameter cannot distinguish the two cases.
    /// </summary>
    private sealed record TenantCacheHolder(Tenant? Tenant);

    private const string CacheKeyPrefix = "tenant:byid:";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantCacheService> _logger;
    private readonly TimeSpan _tenantCacheExpiration;
    private readonly TimeSpan _nullResultCacheExpiration;

    public TenantCacheService(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TenantCacheService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var expirationMinutes = configuration.GetValue<int>("TenantCache:ExpirationMinutes", 5);
        var nullResultExpirationMinutes = configuration.GetValue<int>("TenantCache:NullResultExpirationMinutes", 2);
        _tenantCacheExpiration = TimeSpan.FromMinutes(Math.Max(1, expirationMinutes));
        _nullResultCacheExpiration = TimeSpan.FromMinutes(Math.Max(1, nullResultExpirationMinutes));
    }

    public async Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        var semaphore = _keyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out TenantCacheHolder? cachedHolder))
                return cachedHolder?.Tenant?.ShallowCopy();

            _logger.LogDebug("Cache miss for tenant {TenantId}, fetching from database", tenantId);
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var tenant = await repo.GetByTenantIdAsync(tenantId, cancellationToken);

            var expiration = tenant != null ? _tenantCacheExpiration : _nullResultCacheExpiration;
            var holder = new TenantCacheHolder(tenant);

            var entryOptions = new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetAbsoluteExpiration(expiration);
            _cache.Set(cacheKey, holder, entryOptions);

            _logger.LogDebug("Cached tenant result for {TenantId}", tenantId);
            return holder.Tenant?.ShallowCopy();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void InvalidateTenant(string tenantId)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated tenant cache for {TenantId}", tenantId);
    }
}
