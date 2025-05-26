using System.Collections.Concurrent;

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
}

/// <summary>
/// Memory cache implementation of certificate validation cache
/// </summary>
public class MemoryCertificateValidationCache : ICertificateValidationCache
{
    private static readonly ConcurrentDictionary<string, (DateTime ValidatedAt, bool IsValid)> _cache = new();
    private readonly ILogger<MemoryCertificateValidationCache> _logger;
    private const int CacheExpirationMinutes = 10;

    public MemoryCertificateValidationCache(ILogger<MemoryCertificateValidationCache> logger)
    {
        _logger = logger;
    }

    public (bool found, bool isValid) GetValidation(string thumbprint)
    {
        if (_cache.TryGetValue(thumbprint, out var cacheEntry))
        {
            if (DateTime.UtcNow.Subtract(cacheEntry.ValidatedAt).TotalMinutes < CacheExpirationMinutes)
            {
                _logger.LogDebug("Certificate validation cache hit for thumbprint: {Thumbprint}", thumbprint);
                return (true, cacheEntry.IsValid);
            }
            else
            {
                // Remove expired entry
                _cache.TryRemove(thumbprint, out _);
                _logger.LogDebug("Certificate validation cache entry expired for thumbprint: {Thumbprint}", thumbprint);
            }
        }
        
        _logger.LogDebug("Certificate validation cache miss for thumbprint: {Thumbprint}", thumbprint);
        return (false, false);
    }

    public void CacheValidation(string thumbprint, bool isValid)
    {
        _cache.TryAdd(thumbprint, (DateTime.UtcNow, isValid));
        _logger.LogDebug("Cached certificate validation result for thumbprint: {Thumbprint}, valid: {IsValid}", 
            thumbprint, isValid);
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
        _logger.LogDebug("Certificate validation cache disabled - returning cache miss for thumbprint: {Thumbprint}", thumbprint);
        return (false, false);
    }

    public void CacheValidation(string thumbprint, bool isValid)
    {
        _logger.LogDebug("Certificate validation cache disabled - not caching result for thumbprint: {Thumbprint}", thumbprint);
    }
} 