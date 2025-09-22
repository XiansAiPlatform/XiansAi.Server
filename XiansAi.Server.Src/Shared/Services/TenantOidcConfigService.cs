using System.Text.Json;
using System.Security.Cryptography;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Utils;

namespace Shared.Services;

public class OidcProviderRule
{
    public string? Authority { get; set; }
    public string? Issuer { get; set; }
    public List<string>? ExpectedAudience { get; set; }
    public string? Scope { get; set; }
    public bool? RequireSignedTokens { get; set; }
    public List<string>? AcceptedAlgorithms { get; set; }
    public bool? RequireHttpsMetadata { get; set; }
    public List<CustomClaimCheck>? AdditionalClaims { get; set; }
    public Dictionary<string, object>? ProviderSpecificSettings { get; set; }
}

public class CustomClaimCheck
{
    public string Claim { get; set; } = string.Empty;
    public string Op { get; set; } = "equals"; // equals, contains, not_equals
    public object? Value { get; set; }
}

public class TenantOidcRules
{
    public string TenantId { get; set; } = string.Empty;
    public List<string>? AllowedProviders { get; set; }
    public Dictionary<string, OidcProviderRule>? Providers { get; set; }
    public string? Notes { get; set; }
}

public interface ITenantOidcConfigService
{
    Task<ServiceResult<TenantOidcRules?>> GetForTenantAsync(string tenantId);
    Task<ServiceResult<bool>> UpsertAsync(string tenantId, string jsonConfig, string actorUserId);
    Task<ServiceResult<bool>> DeleteAsync(string tenantId);
    Task<ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>> GetAllAsync();
}

public class TenantOidcConfigService : ITenantOidcConfigService
{
    private readonly ITenantOidcConfigRepository _repository;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<TenantOidcConfigService> _logger;
    private readonly ObjectCache _cache;
    private readonly string _uniqueSecret;
    
    // Cache configuration
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);
    private static readonly string CacheKeyPrefix = "tenant_oidc_config:";

    public TenantOidcConfigService(ITenantOidcConfigRepository repository, ISecureEncryptionService encryption, ILogger<TenantOidcConfigService> logger, IConfiguration configuration, ObjectCache cache)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
        _cache = cache;
        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:TenantOidcSecretKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:TenantOidcSecretKey is not configured. Using the base secret value.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
            {
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            }
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<ServiceResult<TenantOidcRules?>> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<TenantOidcRules?>.BadRequest("tenantId is required");

        try
        {
            // Try to get from cache first
            var cacheKey = GetCacheKey(tenantId);
            var cachedRules = await _cache.GetAsync<TenantOidcRules>(cacheKey);
            
            if (cachedRules != null)
            {
                _logger.LogDebug("Retrieved OIDC config for tenant {TenantId} from cache", tenantId);
                return ServiceResult<TenantOidcRules?>.Success(cachedRules);
            }

            // Check for cached null result (when tenant has no config)
            var nullCacheKey = GetNullCacheKey(tenantId);
            var hasNullResult = await _cache.GetAsync<bool?>(nullCacheKey);
            if (hasNullResult == true)
            {
                _logger.LogDebug("Retrieved null OIDC config for tenant {TenantId} from cache", tenantId);
                return ServiceResult<TenantOidcRules?>.Success(null);
            }

            // Cache miss - fetch from database
            _logger.LogDebug("Cache miss for tenant {TenantId} OIDC config, fetching from database", tenantId);
            var result = await GetForTenantFromDatabaseAsync(tenantId);
            
            // Cache the result based on success/failure
            if (result.IsSuccess)
            {
                if (result.Data != null)
                {
                    await _cache.SetAsync(cacheKey, result.Data, CacheExpiration);
                    _logger.LogDebug("Cached OIDC config for tenant {TenantId} with {CacheExpiration} expiration", 
                        tenantId, CacheExpiration);
                }
                else
                {
                    // Cache null results to avoid repeated database hits for non-existent configs
                    await _cache.SetAsync(nullCacheKey, true, CacheExpiration);
                    _logger.LogDebug("Cached null OIDC config for tenant {TenantId} with {CacheExpiration} expiration", 
                        tenantId, CacheExpiration);
                }
            }
            // Don't cache error results - let them retry on next request
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached OIDC config for tenant {TenantId}", tenantId);
            // Fallback to database on cache errors
            return await GetForTenantFromDatabaseAsync(tenantId);
        }
    }

    private async Task<ServiceResult<TenantOidcRules?>> GetForTenantFromDatabaseAsync(string tenantId)
    {
        try
        {
            var doc = await _repository.GetByTenantIdAsync(tenantId);
            if (doc == null) return ServiceResult<TenantOidcRules?>.Success(null);
            
            try
            {
                var json = _encryption.Decrypt(doc.EncryptedPayload, _uniqueSecret);
                var rules = JsonSerializer.Deserialize<TenantOidcRules>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (rules == null) return ServiceResult<TenantOidcRules?>.Success(null);
                return ServiceResult<TenantOidcRules?>.Success(rules);
            }
            catch (AuthenticationTagMismatchException ex)
            {
                _logger.LogWarning(ex, "Decryption failed due to tag mismatch for tenant {TenantId}", tenantId);
                return ServiceResult<TenantOidcRules?>.Conflict("The encryption keys may have changed. Please reconfigure the tenant OIDC settings.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<TenantOidcRules?>.InternalServerError("Failed to load OIDC configuration");
        }
    }

    public async Task<ServiceResult<bool>> UpsertAsync(string tenantId, string jsonConfig, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<bool>.BadRequest("tenantId is required");
        if (string.IsNullOrWhiteSpace(jsonConfig))
            return ServiceResult<bool>.BadRequest("config json is required");
        try
        {
            // Validate minimal schema: ensure tenantId in JSON matches path
            var rules = JsonSerializer.Deserialize<TenantOidcRules>(jsonConfig, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (rules == null || string.IsNullOrWhiteSpace(rules.TenantId) || !string.Equals(rules.TenantId, tenantId, StringComparison.Ordinal))
            {
                return ServiceResult<bool>.BadRequest("tenantId mismatch or missing in JSON");
            }

            // Enforce provider count constraints: at least 1, at most 5
            var providerCount = rules.Providers?.Count ?? 0;
            if (providerCount < 1)
            {
                return ServiceResult<bool>.BadRequest("At least one provider is required");
            }
            if (providerCount > 5)
            {
                return ServiceResult<bool>.BadRequest("A maximum of 5 providers is allowed");
            }

            // Enforce that each provider must have an issuer
            var missingIssuerProviders = rules.Providers!
                .Where(kv => kv.Value == null || string.IsNullOrWhiteSpace(kv.Value.Issuer))
                .Select(kv => kv.Key)
                .ToList();
            if (missingIssuerProviders.Count > 0)
            {
                var list = string.Join(", ", missingIssuerProviders);
                return ServiceResult<bool>.BadRequest($"Issuer is required for providers: {list}");
            }

            var encrypted = _encryption.Encrypt(jsonConfig, _uniqueSecret);
            var existing = await _repository.GetByTenantIdAsync(tenantId);
            var now = DateTime.UtcNow;
            var doc = existing ?? new TenantOidcConfig
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TenantId = tenantId,
                EncryptedPayload = encrypted,
                CreatedAt = now,
                CreatedBy = actorUserId
            };
            if (existing != null)
            {
                doc.EncryptedPayload = encrypted;
                doc.UpdatedAt = now;
                doc.UpdatedBy = actorUserId;
            }
            await _repository.UpsertAsync(doc);
            
            // Invalidate cache after successful update
            await InvalidateCacheAsync(tenantId);
            _logger.LogDebug("Invalidated cache for tenant {TenantId} after upsert", tenantId);
            
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<bool>.InternalServerError("Failed to save OIDC configuration");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<bool>.BadRequest("tenantId is required");

        try
        {
            var removed = await _repository.DeleteAsync(tenantId);
            
            if (removed)
            {
                // Invalidate cache after successful deletion
                await InvalidateCacheAsync(tenantId);
                _logger.LogDebug("Invalidated cache for tenant {TenantId} after deletion", tenantId);
            }
            
            return removed ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound("No configuration found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<bool>.InternalServerError("Failed to delete OIDC configuration");
        }
    }

    public async Task<ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>> GetAllAsync()
    {
        try
        {
            var all = await _repository.GetAllAsync();
            var list = new List<(string tenantId, TenantOidcRules? rules)>();
            foreach (var item in all)
            {
                try
                {
                    var json = _encryption.Decrypt(item.EncryptedPayload, _uniqueSecret);
                    var rules = JsonSerializer.Deserialize<TenantOidcRules>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    list.Add((item.TenantId, rules));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt/deserialize OIDC config for tenant {TenantId}", item.TenantId);
                    list.Add((item.TenantId, null));
                }
            }
            return ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>.Success(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all OIDC configs");
            return ServiceResult<List<(string tenantId, TenantOidcRules? rules)>>.InternalServerError("Failed to list configs");
        }
    }

    /// <summary>
    /// Generates a cache key for a tenant's OIDC configuration
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <returns>The cache key</returns>
    private static string GetCacheKey(string tenantId)
    {
        return $"{CacheKeyPrefix}{tenantId}";
    }

    /// <summary>
    /// Generates a cache key for tracking null results for a tenant
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <returns>The null cache key</returns>
    private static string GetNullCacheKey(string tenantId)
    {
        return $"{CacheKeyPrefix}{tenantId}:null";
    }

    /// <summary>
    /// Invalidates the cache for a specific tenant
    /// </summary>
    /// <param name="tenantId">The tenant ID</param>
    /// <returns>Task representing the async operation</returns>
    private async Task InvalidateCacheAsync(string tenantId)
    {
        try
        {
            var cacheKey = GetCacheKey(tenantId);
            var nullCacheKey = GetNullCacheKey(tenantId);
            
            // Remove both the data cache and null result cache
            await Task.WhenAll(
                _cache.RemoveAsync(cacheKey),
                _cache.RemoveAsync(nullCacheKey)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for tenant {TenantId}", tenantId);
            // Don't throw - cache invalidation failure shouldn't break the main operation
        }
    }
}

