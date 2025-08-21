using System.Text.Json;
using System.Security.Cryptography;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;

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
    private readonly string _uniqueSecret;

    public TenantOidcConfigService(ITenantOidcConfigRepository repository, ISecureEncryptionService encryption, ILogger<TenantOidcConfigService> logger, IConfiguration configuration)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
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
}

