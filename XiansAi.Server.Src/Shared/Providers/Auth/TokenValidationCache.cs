using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using Shared.Services;
using Shared.Utils;

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

    /// <summary>
    /// Invalidate all cached tokens for a specific user
    /// </summary>
    Task InvalidateUserTokens(string userId);
}

/// <summary>
/// Memory cache implementation of token validation cache
/// </summary>
public class MemoryTokenValidationCache : ITokenValidationCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryTokenValidationCache> _logger;
    private readonly TimeSpan _absoluteExpiration;
    private readonly TimeSpan _slidingExpiration;
    private readonly long _cacheEntrySizeLimit;

    // Which tokens belong to which user, so that locking an account can drop them. Held by the
    // shared singleton rather than by this instance: this cache is registered scoped, so an index
    // of its own would be discarded with the request that cached the token, leaving the request
    // that locks the account with nothing to evict.
    private readonly IUserCacheIndex _userCacheIndex;

    public MemoryTokenValidationCache(
        IMemoryCache cache,
        IUserCacheIndex userCacheIndex,
        ILogger<MemoryTokenValidationCache> logger,
        IConfiguration configuration)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _userCacheIndex = userCacheIndex ?? throw new ArgumentNullException(nameof(userCacheIndex));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Default to 5 minutes absolute expiration if not configured
        _absoluteExpiration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("Auth:TokenValidationCacheDurationMinutes", 5));
        
        // Default to 2 minutes sliding expiration - frequently accessed tokens stay cached longer
        _slidingExpiration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("Auth:TokenValidationSlidingExpirationMinutes", 2));
        
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
        // Set both absolute and sliding expiration for optimal caching
        // Set size to enable eviction policy when cache size limit is configured
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_absoluteExpiration)
            .SetSlidingExpiration(_slidingExpiration)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(_cacheEntrySizeLimit)
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                // Keeps the index from growing without bound as tokens expire on their own.
                _userCacheIndex.Forget(userId, key.ToString() ?? string.Empty);
            });

        var cachedValidation = new CachedValidation
        {
            Valid = valid,
            UserId = userId,
            TenantIds = tenantIds?.ToList() ?? new List<string>()
        };

        _cache.Set(cacheKey, cachedValidation, cacheOptions);
        _userCacheIndex.Track(userId, cacheKey);

        _logger.LogDebug("Cached successful token validation result for user {UserId}", LogSanitizer.Sanitize(userId));
        return Task.CompletedTask;
    }

    private static string GetCacheKey(string token)
    {
        // Use SHA256 hash to avoid collisions and prevent token leakage in cache keys
        // SHA256.HashData is the modern, thread-safe, and performant way in .NET 5+
        // It doesn't require creating instances or using locks
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
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

        // Removing the entry runs the post-eviction callback, which drops it from the index.
        _cache.Remove(GetCacheKey(token));


        _logger.LogDebug("Removed token validation cache entry");
        return Task.CompletedTask;
    }

    public Task InvalidateUserTokens(string userId)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("InvalidateUserTokens called with null or empty userId");
            return Task.CompletedTask;
        }

        _userCacheIndex.Invalidate(userId);
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

    public Task InvalidateUserTokens(string userId)
    {
        return Task.CompletedTask;
    }
}