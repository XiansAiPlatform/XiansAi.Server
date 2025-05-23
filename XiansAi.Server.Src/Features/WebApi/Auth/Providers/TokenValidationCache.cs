using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

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
        // Use SHA256 hash of the full token to ensure uniqueness while avoiding logging the full token
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        var hashString = Convert.ToHexString(hashBytes);
        return $"token_validation:{hashString}";
    }
    
    private class CachedValidation
    {
        public bool Valid { get; set; }
        public string? UserId { get; set; }
        public IEnumerable<string> TenantIds { get; set; } = new List<string>();
    }
} 