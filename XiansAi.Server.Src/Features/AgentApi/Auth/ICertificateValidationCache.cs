using Microsoft.Extensions.Caching.Memory;

namespace Features.AgentApi.Auth;

/// <summary>
/// Interface for certificate validation caching
/// </summary>
public interface ICertificateValidationCache
{
    /// <summary>
    /// Gets validation result from cache if available
    /// </summary>
    (bool found, bool isValid) GetValidation(string thumbprint);
    
    /// <summary>
    /// Caches validation result for future use
    /// </summary>
    void CacheValidation(string thumbprint, bool isValid);
    
    /// <summary>
    /// Removes a cached validation result
    /// </summary>
    void RemoveValidation(string thumbprint);
}

/// <summary>
/// Memory cache implementation of certificate validation cache
/// Uses IMemoryCache for proper size limits, eviction, and memory management
/// </summary>
public class MemoryCertificateValidationCache : ICertificateValidationCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCertificateValidationCache> _logger;
    private readonly TimeSpan _cacheDuration;
    private readonly long _cacheEntrySize;

    public MemoryCertificateValidationCache(
        IMemoryCache cache,
        ILogger<MemoryCertificateValidationCache> logger,
        IConfiguration configuration)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Default to 10 minutes if not configured
        _cacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("AgentApi:CertificateValidationCacheDurationMinutes", 10));
        
        // Default to size of 1 per entry (requires cache to be configured with size limit)
        _cacheEntrySize = configuration.GetValue<long>("AgentApi:CertificateValidationCacheEntrySize", 1);
    }

    public (bool found, bool isValid) GetValidation(string thumbprint)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            _logger.LogWarning("GetValidation called with null or empty thumbprint");
            return (false, false);
        }

        var cacheKey = GetCacheKey(thumbprint);
        
        if (_cache.TryGetValue<bool>(cacheKey, out var isValid))
        {
            _logger.LogDebug("Certificate validation cache hit for thumbprint: {Thumbprint}", thumbprint);
            return (true, isValid);
        }
        
        _logger.LogDebug("Certificate validation cache miss for thumbprint: {Thumbprint}", thumbprint);
        return (false, false);
    }

    public void CacheValidation(string thumbprint, bool isValid)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            _logger.LogWarning("CacheValidation called with null or empty thumbprint");
            return;
        }

        // SECURITY IMPROVEMENT: Only cache successful validations to prevent cache pollution
        // Invalid certificates should always be re-validated (could be temporarily invalid)
        if (!isValid)
        {
            _logger.LogDebug("Skipping cache for invalid certificate validation result for thumbprint: {Thumbprint}", thumbprint);
            return;
        }

        var cacheKey = GetCacheKey(thumbprint);
        
        // Use Normal priority to allow proper cache eviction when under memory pressure
        // Set size to enable eviction policy when cache size limit is configured
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheDuration)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(_cacheEntrySize);
        
        _cache.Set(cacheKey, isValid, cacheOptions);
        _logger.LogDebug("Cached successful certificate validation for thumbprint: {Thumbprint}", thumbprint);
    }

    public void RemoveValidation(string thumbprint)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            _logger.LogWarning("RemoveValidation called with null or empty thumbprint");
            return;
        }

        var cacheKey = GetCacheKey(thumbprint);
        _cache.Remove(cacheKey);
        _logger.LogDebug("Removed certificate validation cache entry for thumbprint: {Thumbprint}", thumbprint);
    }

    private static string GetCacheKey(string thumbprint)
    {
        // Use a prefixed key to avoid collisions with other cache entries
        return $"cert_validation:{thumbprint}";
    }
}

/// <summary>
/// No-op implementation of certificate validation cache that disables caching
/// </summary>
public class NoOpCertificateValidationCache : ICertificateValidationCache
{
    private readonly ILogger<NoOpCertificateValidationCache> _logger;

    public NoOpCertificateValidationCache(ILogger<NoOpCertificateValidationCache> logger)
    {
        _logger = logger;
    }

    public (bool found, bool isValid) GetValidation(string thumbprint)
    {
        return (false, false);
    }

    public void CacheValidation(string thumbprint, bool isValid)
    {
        // No-op: caching is disabled
    }

    public void RemoveValidation(string thumbprint)
    {
        // No-op: caching is disabled
    }
} 