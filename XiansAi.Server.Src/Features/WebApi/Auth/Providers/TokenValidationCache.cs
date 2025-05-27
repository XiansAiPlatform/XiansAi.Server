using Microsoft.Extensions.Caching.Memory;

namespace Features.WebApi.Auth.Providers;

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
}

/// <summary>
/// Memory cache implementation of token validation cache
/// </summary>
public class MemoryTokenValidationCache : ITokenValidationCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryTokenValidationCache> _logger;
    private readonly TimeSpan _cacheDuration;
    
    public MemoryTokenValidationCache(
        IMemoryCache cache, 
        ILogger<MemoryTokenValidationCache> logger,
        IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        
        // Default to 5 minutes if not configured
        _cacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("Auth:TokenValidationCacheDurationMinutes", 5));
    }
    
    public Task<(bool found, bool valid, string? userId, IEnumerable<string>? tenantIds)> GetValidation(string token)
    {
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
        var cacheKey = GetCacheKey(token);
        
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheDuration)
            .SetPriority(CacheItemPriority.High);
            
        _cache.Set(cacheKey, new CachedValidation
        {
            Valid = valid,
            UserId = userId,
            TenantIds = tenantIds?.ToList() ?? new List<string>()
        }, cacheOptions);
        
        _logger.LogDebug("Cached token validation result");
        return Task.CompletedTask;
    }
    
    private string GetCacheKey(string token)
    {
        // Only use the first 8 chars of the token for the cache key to avoid logging the full token
        var truncatedToken = token.Length > 8 ? token.Substring(0, 8) : token;
        return $"token_validation:{truncatedToken}";
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
        _logger.LogDebug("Token validation cache disabled - returning cache miss");
        return Task.FromResult((false, false, (string?)null, (IEnumerable<string>?)null));
    }
    
    public Task CacheValidation(string token, bool valid, string? userId, IEnumerable<string>? tenantIds)
    {
        _logger.LogDebug("Token validation cache disabled - not caching result");
        return Task.CompletedTask;
    }
} 