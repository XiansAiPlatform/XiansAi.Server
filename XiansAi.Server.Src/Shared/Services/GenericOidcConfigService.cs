using System.Text.Json;
using System.Security.Cryptography;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Generic OIDC Rules - for general tenant-wide authentication
/// </summary>
public class GenericOidcRules
{
    public string TenantId { get; set; } = string.Empty;
    public List<string>? AllowedProviders { get; set; }
    public Dictionary<string, OidcProviderRule>? Providers { get; set; }
    public string? Notes { get; set; }
}

public interface IGenericOidcConfigService
{
    Task<ServiceResult<GenericOidcRules?>> GetForTenantAsync(string tenantId);
    Task<ServiceResult<bool>> UpsertAsync(string tenantId, string jsonConfig, string actorUserId);
    Task<ServiceResult<bool>> DeleteAsync(string tenantId);
    Task<ServiceResult<List<(string tenantId, GenericOidcRules? rules)>>> GetAllAsync();
}

public class GenericOidcConfigService : IGenericOidcConfigService
{
    private readonly IGenericOidcConfigRepository _repository;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<GenericOidcConfigService> _logger;
    private readonly ObjectCache _cache;
    private readonly string _uniqueSecret;
    
    // Cache configuration
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);
    private static readonly string CacheKeyPrefix = "generic_oidc_config:";

    public GenericOidcConfigService(
        IGenericOidcConfigRepository repository, 
        ISecureEncryptionService encryption, 
        ILogger<GenericOidcConfigService> logger, 
        IConfiguration configuration, 
        ObjectCache cache)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
        _cache = cache;
        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:GenericOidcSecretKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:GenericOidcSecretKey is not configured. Using the base secret value.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
            {
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            }
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<ServiceResult<GenericOidcRules?>> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<GenericOidcRules?>.BadRequest("tenantId is required");

        try
        {
            // Try to get from cache first
            var cacheKey = GetCacheKey(tenantId);
            var cachedRules = await _cache.GetAsync<GenericOidcRules>(cacheKey);
            
            if (cachedRules != null)
            {
                _logger.LogDebug("Retrieved Generic OIDC config for tenant {TenantId} from cache", tenantId);
                return ServiceResult<GenericOidcRules?>.Success(cachedRules);
            }

            // Check for cached null result (when tenant has no config)
            var nullCacheKey = GetNullCacheKey(tenantId);
            var hasNullResult = await _cache.GetAsync<bool?>(nullCacheKey);
            if (hasNullResult == true)
            {
                _logger.LogDebug("Retrieved null Generic OIDC config for tenant {TenantId} from cache", tenantId);
                return ServiceResult<GenericOidcRules?>.Success(null);
            }

            // Cache miss - fetch from database
            _logger.LogDebug("Cache miss for tenant {TenantId} Generic OIDC config, fetching from database", tenantId);
            var result = await GetForTenantFromDatabaseAsync(tenantId);
            
            // Cache the result based on success/failure
            if (result.IsSuccess)
            {
                if (result.Data != null)
                {
                    await _cache.SetAsync(cacheKey, result.Data, CacheExpiration);
                    _logger.LogDebug("Cached Generic OIDC config for tenant {TenantId} with {CacheExpiration} expiration", 
                        tenantId, CacheExpiration);
                }
                else
                {
                    // Cache null results to avoid repeated database hits for non-existent configs
                    await _cache.SetAsync(nullCacheKey, true, CacheExpiration);
                    _logger.LogDebug("Cached null Generic OIDC config for tenant {TenantId} with {CacheExpiration} expiration", 
                        tenantId, CacheExpiration);
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached Generic OIDC config for tenant {TenantId}", tenantId);
            // Fallback to database on cache errors
            return await GetForTenantFromDatabaseAsync(tenantId);
        }
    }

    private async Task<ServiceResult<GenericOidcRules?>> GetForTenantFromDatabaseAsync(string tenantId)
    {
        try
        {
            var doc = await _repository.GetByTenantIdAsync(tenantId);
            if (doc == null) return ServiceResult<GenericOidcRules?>.Success(null);
            
            try
            {
                var json = _encryption.Decrypt(doc.EncryptedPayload, _uniqueSecret);
                var rules = JsonSerializer.Deserialize<GenericOidcRules>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (rules == null) return ServiceResult<GenericOidcRules?>.Success(null);
                return ServiceResult<GenericOidcRules?>.Success(rules);
            }
            catch (AuthenticationTagMismatchException ex)
            {
                _logger.LogWarning(ex, "Decryption failed due to tag mismatch for tenant {TenantId}", tenantId);
                return ServiceResult<GenericOidcRules?>.Conflict("The encryption keys may have changed. Please reconfigure the Generic OIDC settings.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Generic OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<GenericOidcRules?>.InternalServerError("Failed to load Generic OIDC configuration");
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
            var rules = JsonSerializer.Deserialize<GenericOidcRules>(jsonConfig, new JsonSerializerOptions
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
            var doc = existing ?? new GenericOidcConfig
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
            _logger.LogInformation("Generic OIDC configuration updated for tenant {TenantId} by {User}", tenantId, actorUserId);
            
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting Generic OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<bool>.InternalServerError("Failed to save Generic OIDC configuration");
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
                _logger.LogInformation("Generic OIDC configuration deleted for tenant {TenantId}", tenantId);
            }
            
            return removed ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound("No configuration found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Generic OIDC config for tenant {TenantId}", tenantId);
            return ServiceResult<bool>.InternalServerError("Failed to delete Generic OIDC configuration");
        }
    }

    public async Task<ServiceResult<List<(string tenantId, GenericOidcRules? rules)>>> GetAllAsync()
    {
        try
        {
            var all = await _repository.GetAllAsync();
            var list = new List<(string tenantId, GenericOidcRules? rules)>();
            foreach (var item in all)
            {
                try
                {
                    var json = _encryption.Decrypt(item.EncryptedPayload, _uniqueSecret);
                    var rules = JsonSerializer.Deserialize<GenericOidcRules>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    list.Add((item.TenantId, rules));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt/deserialize Generic OIDC config for tenant {TenantId}", item.TenantId);
                    list.Add((item.TenantId, null));
                }
            }
            return ServiceResult<List<(string tenantId, GenericOidcRules? rules)>>.Success(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all Generic OIDC configs");
            return ServiceResult<List<(string tenantId, GenericOidcRules? rules)>>.InternalServerError("Failed to list configs");
        }
    }

    private static string GetCacheKey(string tenantId)
    {
        return $"{CacheKeyPrefix}{tenantId}";
    }

    private static string GetNullCacheKey(string tenantId)
    {
        return $"{CacheKeyPrefix}{tenantId}:null";
    }

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
        }
    }
}

