using Microsoft.Extensions.Caching.Memory;
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
    /// Callers must treat returned Tenant instances as read-only; mutating them would corrupt the shared cache.
    /// </summary>
    Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached tenant when it is updated or deleted.
    /// </summary>
    void InvalidateTenant(string tenantId);
}

public class TenantCacheService : ITenantCacheService
{
    private const string CacheKeyPrefix = "tenant:byid:";
    private static readonly TimeSpan TenantCacheExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NullResultCacheExpiration = TimeSpan.FromMinutes(2);

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantCacheService> _logger;

    public TenantCacheService(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        ILogger<TenantCacheService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out Tenant? cachedTenant))
        {
            _logger.LogDebug("Retrieved tenant {TenantId} from cache", tenantId);
            return cachedTenant;
        }

        _logger.LogDebug("Cache miss for tenant {TenantId}, fetching from database", tenantId);
        using var scope = _scopeFactory.CreateScope();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var tenant = await tenantRepository.GetByTenantIdAsync(tenantId);

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSize(1);

        if (tenant != null)
        {
            cacheOptions.SetAbsoluteExpiration(TenantCacheExpiration);
            _cache.Set(cacheKey, tenant, cacheOptions);
            _logger.LogDebug("Cached tenant {TenantId} with {CacheExpiration} expiration", tenantId, TenantCacheExpiration);
        }
        else
        {
            cacheOptions.SetAbsoluteExpiration(NullResultCacheExpiration);
            _cache.Set(cacheKey, (Tenant?)null, cacheOptions);
            _logger.LogDebug("Cached null tenant result for {TenantId}", tenantId);
        }

        return tenant;
    }

    public void InvalidateTenant(string tenantId)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated tenant cache for {TenantId}", tenantId);
    }
}
