using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using System.Text.Json;
using System.Security.Cryptography;
using SecretModel = Shared.Data.Models.Secret;

namespace Shared.Providers.Secret;

/// <summary>
/// Database provider implementation using MongoDB with encryption at rest.
/// </summary>
public class DatabaseSecretVaultProvider : ISecretVaultProvider
{
    private readonly ISecretRepository _repository;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<DatabaseSecretVaultProvider> _logger;
    private readonly string _uniqueSecret;

    public DatabaseSecretVaultProvider(
        ISecretRepository repository,
        ISecureEncryptionService encryption,
        ILogger<DatabaseSecretVaultProvider> logger,
        IConfiguration configuration)
    {
        _repository = repository;
        _encryption = encryption;
        _logger = logger;
        
        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:SecretVaultKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:SecretVaultKey is not configured. Using the base secret value.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
            {
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            }
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<SecretData?> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            var doc = await _repository.GetByScopesAsync(secretId, tenantId, agentId, userId);
            if (doc == null) return null;

            return DecryptSecret(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting secret {SecretId} from database", secretId);
            throw;
        }
    }

    public async Task<(List<SecretData> items, int totalCount)> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize)
    {
        try
        {
            var (docs, totalCount) = await _repository.ListByScopesAsync(tenantId, agentId, userId, secretIdPattern, page, pageSize);
            
            var items = new List<SecretData>();
            foreach (var doc in docs)
            {
                try
                {
                    var secretData = DecryptSecret(doc);
                    if (secretData != null)
                    {
                        // Remove sensitive fields for list responses
                        items.Add(new SecretData
                        {
                            SecretId = secretData.SecretId,
                            TenantId = secretData.TenantId,
                            AgentId = secretData.AgentId,
                            UserId = secretData.UserId,
                            SecretValue = string.Empty, // Required field, but empty for list responses
                            Description = secretData.Description,
                            CreatedAt = secretData.CreatedAt,
                            CreatedBy = secretData.CreatedBy,
                            UpdatedAt = secretData.UpdatedAt,
                            UpdatedBy = secretData.UpdatedBy
                            // metadata, expireAt are not included in list responses
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decrypt secret {SecretId} in list operation", doc.SecretId);
                    // Continue with other secrets
                }
            }

            return (items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secrets from database");
            throw;
        }
    }

    public async Task<bool> SecretExistsAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            return await _repository.ExistsByScopesAsync(secretId, tenantId, agentId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of secret {SecretId} in database", secretId);
            throw;
        }
    }

    public async Task CreateSecretAsync(SecretData secret)
    {
        try
        {
            var exists = await _repository.ExistsByScopesAsync(secret.SecretId, secret.TenantId, secret.AgentId, secret.UserId);
            if (exists)
            {
                throw new InvalidOperationException($"Secret with ID '{secret.SecretId}' already exists in this scope");
            }

            var doc = EncryptSecret(secret);
            await _repository.CreateAsync(doc);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret {SecretId} in database", secret.SecretId);
            throw;
        }
    }

    public async Task UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates)
    {
        try
        {
            var existing = await _repository.GetByScopesAsync(secretId, tenantId, agentId, userId);
            if (existing == null)
            {
                throw new InvalidOperationException($"Secret with ID '{secretId}' not found");
            }

            // Decrypt existing secret
            var existingData = DecryptSecret(existing);

            // Apply updates - only update fields that are explicitly provided
            // Note: SecretValue is required in SecretData, so endpoints set it to empty string
            // when not provided. We use a special check: if it's exactly empty string AND
            // the existing value is not empty, treat it as "no update" sentinel.
            // This allows empty string to be set as a secret value if explicitly provided.
            var shouldPreserveSecretValue = string.IsNullOrEmpty(updates.SecretValue) && 
                                           !string.IsNullOrEmpty(existingData.SecretValue);
            
            var updatedData = new SecretData
            {
                SecretId = existingData!.SecretId,
                TenantId = updates.TenantId ?? existingData.TenantId,
                AgentId = updates.AgentId ?? existingData.AgentId,
                UserId = updates.UserId ?? existingData.UserId,
                // Preserve existing value if empty string was used as sentinel, otherwise use provided value
                SecretValue = shouldPreserveSecretValue ? existingData.SecretValue : updates.SecretValue,
                Metadata = updates.Metadata ?? existingData.Metadata,
                Description = updates.Description ?? existingData.Description,
                ExpireAt = updates.ExpireAt ?? existingData.ExpireAt,
                CreatedAt = existingData.CreatedAt,
                CreatedBy = existingData.CreatedBy,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updates.UpdatedBy ?? existingData.UpdatedBy
            };

            // If scopes changed, we need to delete old and create new (due to unique index)
            if (updatedData.TenantId != existingData.TenantId ||
                updatedData.AgentId != existingData.AgentId ||
                updatedData.UserId != existingData.UserId)
            {
                await _repository.DeleteByScopesAsync(secretId, tenantId, agentId, userId);
                var newDoc = EncryptSecret(updatedData);
                await _repository.CreateAsync(newDoc);
            }
            else
            {
                var updatedDoc = EncryptSecret(updatedData);
                updatedDoc.Id = existing.Id; // Preserve MongoDB ID
                await _repository.UpdateAsync(updatedDoc);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating secret {SecretId} in database", secretId);
            throw;
        }
    }

    public async Task<bool> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            return await _repository.DeleteByScopesAsync(secretId, tenantId, agentId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret {SecretId} from database", secretId);
            throw;
        }
    }

    private SecretModel EncryptSecret(SecretData secretData)
    {
        var encryptedSecretValue = _encryption.Encrypt(secretData.SecretValue, _uniqueSecret);
        var encryptedMetadata = string.IsNullOrWhiteSpace(secretData.Metadata)
            ? null
            : _encryption.Encrypt(secretData.Metadata, _uniqueSecret);
        var encryptedExpireAt = secretData.ExpireAt.HasValue
            ? _encryption.Encrypt(secretData.ExpireAt.Value.ToString("O"), _uniqueSecret)
            : null;

        return new SecretModel
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            SecretId = secretData.SecretId,
            TenantId = secretData.TenantId,
            AgentId = secretData.AgentId,
            UserId = secretData.UserId,
            EncryptedSecretValue = encryptedSecretValue,
            EncryptedMetadata = encryptedMetadata,
            EncryptedExpireAt = encryptedExpireAt,
            Description = secretData.Description,
            CreatedAt = secretData.CreatedAt,
            CreatedBy = secretData.CreatedBy,
            UpdatedAt = secretData.UpdatedAt,
            UpdatedBy = secretData.UpdatedBy
        };
    }

    private SecretData DecryptSecret(SecretModel doc)
    {
        var secretValue = _encryption.Decrypt(doc.EncryptedSecretValue, _uniqueSecret);
        var metadata = string.IsNullOrWhiteSpace(doc.EncryptedMetadata)
            ? null
            : _encryption.Decrypt(doc.EncryptedMetadata, _uniqueSecret);
        var expireAtStr = string.IsNullOrWhiteSpace(doc.EncryptedExpireAt)
            ? null
            : _encryption.Decrypt(doc.EncryptedExpireAt, _uniqueSecret);
        var expireAt = string.IsNullOrWhiteSpace(expireAtStr)
            ? null
            : DateTime.TryParse(expireAtStr, out var dt) ? dt : (DateTime?)null;

        return new SecretData
        {
            SecretId = doc.SecretId,
            TenantId = doc.TenantId,
            AgentId = doc.AgentId,
            UserId = doc.UserId,
            SecretValue = secretValue,
            Metadata = metadata,
            Description = doc.Description,
            ExpireAt = expireAt,
            CreatedAt = doc.CreatedAt,
            CreatedBy = doc.CreatedBy,
            UpdatedAt = doc.UpdatedAt,
            UpdatedBy = doc.UpdatedBy
        };
    }
}

