using Shared.Data.Models;
using Shared.Providers.Secret;
using Shared.Utils.Services;

namespace Shared.Services;

public interface ISecretVaultService
{
    Task<ServiceResult<SecretData?>> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, bool includeValue = false);
    Task<ServiceResult<(List<SecretData> items, int totalCount)>> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize);
    Task<ServiceResult<bool>> CreateSecretAsync(SecretData secret, string actorUserId);
    Task<ServiceResult<bool>> UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates, string actorUserId);
    Task<ServiceResult<bool>> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId);
}

public class SecretVaultService : ISecretVaultService
{
    private readonly ISecretVaultProvider _provider;
    private readonly ILogger<SecretVaultService> _logger;

    public SecretVaultService(ISecretVaultProvider provider, ILogger<SecretVaultService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<ServiceResult<SecretData?>> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, bool includeValue = false)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return ServiceResult<SecretData?>.BadRequest("secretId is required");
        }

        try
        {
            var secret = await _provider.GetSecretAsync(secretId, tenantId, agentId, userId);
            if (secret == null)
            {
                return ServiceResult<SecretData?>.NotFound($"Secret with ID '{secretId}' not found");
            }

            // If includeValue is false, remove sensitive fields
            if (!includeValue)
            {
                secret = new SecretData
                {
                    SecretId = secret.SecretId,
                    TenantId = secret.TenantId,
                    AgentId = secret.AgentId,
                    UserId = secret.UserId,
                    SecretValue = string.Empty, // Required field, but empty for non-value responses
                    Description = secret.Description,
                    CreatedAt = secret.CreatedAt,
                    CreatedBy = secret.CreatedBy,
                    UpdatedAt = secret.UpdatedAt,
                    UpdatedBy = secret.UpdatedBy
                    // metadata, expireAt are excluded
                };
            }

            return ServiceResult<SecretData?>.Success(secret);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting secret {SecretId}", secretId);
            return ServiceResult<SecretData?>.InternalServerError("Failed to retrieve secret");
        }
    }

    public async Task<ServiceResult<(List<SecretData> items, int totalCount)>> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        try
        {
            var result = await _provider.ListSecretsAsync(tenantId, agentId, userId, secretIdPattern, page, pageSize);
            return ServiceResult<(List<SecretData> items, int totalCount)>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secrets");
            return ServiceResult<(List<SecretData> items, int totalCount)>.InternalServerError("Failed to list secrets");
        }
    }

    public async Task<ServiceResult<bool>> CreateSecretAsync(SecretData secret, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(secret.SecretId))
        {
            return ServiceResult<bool>.BadRequest("secretId is required");
        }

        if (string.IsNullOrWhiteSpace(secret.SecretValue))
        {
            return ServiceResult<bool>.BadRequest("secretValue is required");
        }

        try
        {
            // Set audit fields
            secret.CreatedAt = DateTime.UtcNow;
            secret.CreatedBy = actorUserId;

            await _provider.CreateSecretAsync(secret);
            return ServiceResult<bool>.Success(true);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to create secret {SecretId}: {Message}", secret.SecretId, ex.Message);
            return ServiceResult<bool>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret {SecretId}", secret.SecretId);
            return ServiceResult<bool>.InternalServerError("Failed to create secret");
        }
    }

    public async Task<ServiceResult<bool>> UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates, string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return ServiceResult<bool>.BadRequest("secretId is required");
        }

        try
        {
            // Set audit fields
            updates.UpdatedAt = DateTime.UtcNow;
            updates.UpdatedBy = actorUserId;

            await _provider.UpdateSecretAsync(secretId, tenantId, agentId, userId, updates);
            return ServiceResult<bool>.Success(true);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to update secret {SecretId}: {Message}", secretId, ex.Message);
            return ServiceResult<bool>.NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating secret {SecretId}", secretId);
            return ServiceResult<bool>.InternalServerError("Failed to update secret");
        }
    }

    public async Task<ServiceResult<bool>> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(secretId))
        {
            return ServiceResult<bool>.BadRequest("secretId is required");
        }

        try
        {
            var deleted = await _provider.DeleteSecretAsync(secretId, tenantId, agentId, userId);
            if (!deleted)
            {
                return ServiceResult<bool>.NotFound($"Secret with ID '{secretId}' not found");
            }

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret {SecretId}", secretId);
            return ServiceResult<bool>.InternalServerError("Failed to delete secret");
        }
    }
}

