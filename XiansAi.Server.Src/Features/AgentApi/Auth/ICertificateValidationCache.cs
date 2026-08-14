using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using Shared.Services;
using Shared.Utils;

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
    private readonly IUserCacheIndex _userCacheIndex;
    private readonly ILogger<MemoryCertificateValidationCache> _logger;
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _cacheMaxDuration;
    private readonly long _cacheEntrySize;

    public MemoryCertificateValidationCache(
        IMemoryCache cache,
        IUserCacheIndex userCacheIndex,
        ILogger<MemoryCertificateValidationCache> logger,
        IConfiguration configuration)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _userCacheIndex = userCacheIndex ?? throw new ArgumentNullException(nameof(userCacheIndex));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Idle window: an agent that keeps calling within this window never revalidates.
        _cacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("AgentApi:CertificateValidationCacheDurationMinutes", 2));

        // Hard ceiling regardless of activity. Revoking a certificate and disabling the account
        // both evict the entry directly, so this only bounds staleness on the server instances
        // that did not handle that request. Matched to the token and role caches, so that every
        // cached authorization decision has the same worst case on the instances it did not reach.
        _cacheMaxDuration = TimeSpan.FromMinutes(
            configuration.GetValue<double>("AgentApi:CertificateValidationCacheMaxDurationMinutes", 5));

        if (_cacheMaxDuration < _cacheDuration)
        {
            _logger.LogWarning(
                "CertificateValidationCacheMaxDurationMinutes ({MaxDuration}) is below CertificateValidationCacheDurationMinutes ({Duration}); using the idle window as the ceiling",
                _cacheMaxDuration, _cacheDuration);
            _cacheMaxDuration = _cacheDuration;
        }
        
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
        
        // Sliding expiration keeps actively used agent certificates warm (a busy agent never pays
        // the revoke check + chain build + tenant lookup), while the absolute expiration caps how
        // long a stale entry can survive.
        // Use Normal priority to allow proper cache eviction when under memory pressure
        // Set size to enable eviction policy when cache size limit is configured
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(_cacheDuration)
            .SetAbsoluteExpiration(_cacheMaxDuration)
            .SetPriority(CacheItemPriority.Normal)
            .SetSize(_cacheEntrySize)
            .RegisterPostEvictionCallback(
                (key, _, _, _) => _userCacheIndex.Forget(validation.UserId, key.ToString() ?? string.Empty));
        
        _cache.Set(cacheKey, validation, cacheOptions);

        // Tracked against the account the certificate resolved to, so that disabling that account
        // stops its agents rather than leaving them running until the entry ages out. The entry
        // holds the roles and the SysAdmin flag, so a cache hit never revisits the record.
        _userCacheIndex.Track(validation.UserId, cacheKey);

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
        _logger.LogDebug("Removed certificate validation cache entry for thumbprint: {Thumbprint}", LogSanitizer.Sanitize(thumbprint));
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