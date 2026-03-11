using Microsoft.Extensions.Caching.Memory;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Caches tenant lookups by tenant ID to reduce database load for frequently accessed tenants.
/// Used primarily by Admin API auth when SysAdmin specifies a target tenant.
/// </summary>
public interface ITenantCacheService
{
    /// <summary>
    /// Gets a tenant by ID from cache or database.
    /// Null results are cached with a shorter TTL to mitigate brute-force enumeration.
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
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<TenantCacheService> _logger;

    public TenantCacheService(
        IMemoryCache cache,
        ITenantRepository tenantRepository,
        ILogger<TenantCacheService> logger)
    {
        _cache = cache;
        _tenantRepository = tenantRepository;
        _logger = logger;
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
        var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);

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
