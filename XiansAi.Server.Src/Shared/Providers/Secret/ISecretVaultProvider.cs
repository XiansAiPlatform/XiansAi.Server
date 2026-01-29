using Shared.Data.Models;

namespace Shared.Providers.Secret;

/// <summary>
/// Interface for secret vault providers (Database, Azure Key Vault, AWS Secrets Manager, HashiCorp Vault).
/// </summary>
public interface ISecretVaultProvider
{
    /// <summary>
    /// Gets a secret by its scopes.
    /// </summary>
    Task<SecretData?> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId);

    /// <summary>
    /// Lists secrets matching the provided scopes and filters.
    /// </summary>
    Task<(List<SecretData> items, int totalCount)> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize);

    /// <summary>
    /// Checks if a secret exists in the given scope.
    /// </summary>
    Task<bool> SecretExistsAsync(string secretId, string? tenantId, string? agentId, string? userId);

    /// <summary>
    /// Creates a new secret.
    /// </summary>
    Task CreateSecretAsync(SecretData secret);

    /// <summary>
    /// Updates an existing secret. Only provided fields are updated.
    /// </summary>
    Task UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates);

    /// <summary>
    /// Deletes a secret by its scopes.
    /// </summary>
    Task<bool> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId);
}

