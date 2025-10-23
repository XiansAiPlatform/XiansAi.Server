using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace Shared.Providers.Auth;

/// <summary>
/// Interface for token validation caching
/// </summary>
public interface ITokenValidationCache
{
    /// <summary>
    /// Gets validation result from cache if available
    /// </summary>
    Task<(bool found, bool valid, string? userId, IEnumerable<string>? tenantIds)> GetValidation(string token);

    /// <summary>
    /// Caches validation result for future use
    /// </summary>
    Task CacheValidation(string token, bool valid, string? userId, IEnumerable<string>? tenantIds);

    /// <summary>
    /// Remove the cached validation for a token
    /// </summary>
    Task RemoveValidation(string token);
}

/// <summary>
/// Memory cache implementation of token validation cache
/// </summary>
public class MemoryTokenValidationCache : ITokenValidationCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryTokenValidationCache> _logger;
    private readonly TimeSpan _cacheDuration;
    private readonly long _cacheEntrySizeLimit;
    
    // Shared static SHA256 instance for performance (thread-safe in .NET)
    private static readonly SHA256 _sha256 = SHA256.Create();
    private static readonly object _hashLock = new object();

    public MemoryTokenValidationCache(
        IMemoryCache cache,
        ILogger<MemoryTokenValidationCache> logger,
        IConfiguration configuration)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Default to 5 minutes if not configured
        _cacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("Auth:TokenValidationCacheDurationMinutes", 5));
        
        // Default to size of 1 per entry (requires cache to be configured with size limit)
        _cacheEntrySizeLimit = configuration.GetValue<long>("Auth:TokenValidationCacheEntrySize", 1);
    }

    public Task<(bool found, bool valid, string? userId, IEnumerable<string>? tenantIds)> GetValidation(string token)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("GetValidation called with null or empty token");
            return Task.FromResult((false, false, (string?)null, (IEnumerable<string>?)null));
        }

        var cacheKey = GetCacheKey(token);

        if (_cache.TryGetValue<CachedValidation>(cacheKey, out var cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("Token validation cache hit");
            return Task.FromResult((
                true,
                cachedResult.Valid,
                cachedResult.UserId,
                (IEnumerable<string>?)cachedResult.TenantIds));
        }

        _logger.LogDebug("Token validation cache miss");
        return Task.FromResult((false, false, (string?)null, (IEnumerable<string>?)null));
    }

    public Task CacheValidation(string token, bool valid, string? userId, IEnumerable<string>? tenantIds)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("CacheValidation called with null or empty token");
            return Task.CompletedTask;
        }

        // SECURITY IMPROVEMENT: Only cache successful validations to prevent cache pollution
        // and reduce the attack surface. Failed validations should always be re-validated.
        if (!valid || string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Skipping cache for invalid token validation result");
            return Task.CompletedTask;
        }

        var cacheKey = GetCacheKey(token);

        // Use Normal priority to allow proper cache eviction when under memory pressure
        // Set size to enable eviction policy when cache size limit is configured
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheDuration)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(_cacheEntrySizeLimit);

        _cache.Set(cacheKey, new CachedValidation
        {
            Valid = valid,
            UserId = userId,
            TenantIds = tenantIds?.ToList() ?? new List<string>()
        }, cacheOptions);

        _logger.LogDebug("Cached successful token validation result");
        return Task.CompletedTask;
    }

    private string GetCacheKey(string token)
    {
        // Use SHA256 hash to avoid collisions and not log the full token
        // Use shared static instance with lock for thread-safety and performance
        byte[] hashBytes;
        lock (_hashLock)
        {
            hashBytes = _sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        }
        var hash = Convert.ToBase64String(hashBytes);
        return $"token_validation:{hash}";
    }

    public Task RemoveValidation(string token)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("RemoveValidation called with null or empty token");
            return Task.CompletedTask;
        }

        var cacheKey = GetCacheKey(token);
        _cache.Remove(cacheKey);
        _logger.LogDebug("Removed token validation cache entry");
        return Task.CompletedTask;
    }

    private class CachedValidation
    {
        public bool Valid { get; set; }
        public string? UserId { get; set; }
        public IEnumerable<string> TenantIds { get; set; } = new List<string>();
    }
}

/// <summary>
/// No-op implementation of token validation cache that disables caching
/// </summary>
public class NoOpTokenValidationCache : ITokenValidationCache
{
    private readonly ILogger<NoOpTokenValidationCache> _logger;

    public NoOpTokenValidationCache(ILogger<NoOpTokenValidationCache> logger)
    {
        _logger = logger;
    }

    public Task<(bool found, bool valid, string? userId, IEnumerable<string>? tenantIds)> GetValidation(string token)
    {
        return Task.FromResult((false, false, (string?)null, (IEnumerable<string>?)null));
    }

    public Task CacheValidation(string token, bool valid, string? userId, IEnumerable<string>? tenantIds)
    {
        return Task.CompletedTask;
    }

    public Task RemoveValidation(string token)
    {
        return Task.CompletedTask;
    }
}