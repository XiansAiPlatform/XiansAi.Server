using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;

namespace Features.AgentApi.Auth;

/// <summary>
/// Cached data for a validated certificate.
/// Includes minimal user/tenant context so cache hits can avoid DB calls.
/// </summary>
public record CachedCertificateValidation
{
    public bool IsValid { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public bool IsSysAdmin { get; init; }
}

/// <summary>
/// Interface for certificate validation caching
/// </summary>
public interface ICertificateValidationCache
{
    /// <summary>
    /// Gets validation result from cache if available
    /// </summary>
    (bool found, CachedCertificateValidation? validation) GetValidation(string thumbprint);
    
    /// <summary>
    /// Caches validation result for future use
    /// </summary>
    void CacheValidation(string thumbprint, CachedCertificateValidation validation);
    
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

    public (bool found, CachedCertificateValidation? validation) GetValidation(string thumbprint)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            _logger.LogWarning("GetValidation called with null or empty thumbprint");
            return (false, null);
        }

        var cacheKey = GetCacheKey(thumbprint);
        
        if (_cache.TryGetValue<CachedCertificateValidation>(cacheKey, out var validation))
        {
            return (true, validation);
        }
        
        _logger.LogDebug("Certificate validation cache miss for thumbprint: {Thumbprint}", thumbprint);
        return (false, null);
    }

    public void CacheValidation(string thumbprint, CachedCertificateValidation validation)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            _logger.LogWarning("CacheValidation called with null or empty thumbprint");
            return;
        }

        // Only cache successful validations to prevent cache pollution
        if (!validation.IsValid)
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
        
        _cache.Set(cacheKey, validation, cacheOptions);
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

    public (bool found, CachedCertificateValidation? validation) GetValidation(string thumbprint)
    {
        return (false, null);
    }

    public void CacheValidation(string thumbprint, CachedCertificateValidation validation)
    {
        // No-op: caching is disabled
    }

    public void RemoveValidation(string thumbprint)
    {
        // No-op: caching is disabled
    }
} 