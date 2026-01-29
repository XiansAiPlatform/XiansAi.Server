using Shared.Data.Models;

namespace Shared.Providers.Secret;

/// <summary>
/// HashiCorp Vault provider implementation.
/// Stores the entire SecretData object as serialized JSON in HashiCorp Vault.
/// </summary>
public class HashiCorpVaultProvider : ISecretVaultProvider
{
    private readonly ILogger<HashiCorpVaultProvider> _logger;

    public HashiCorpVaultProvider(ILogger<HashiCorpVaultProvider> logger)
    {
        _logger = logger;
    }

    public Task<SecretData?> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }

    public Task<(List<SecretData> items, int totalCount)> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }

    public Task<bool> SecretExistsAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }

    public Task CreateSecretAsync(SecretData secret)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }

    public Task UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }

    public Task<bool> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("HashiCorp Vault provider is not yet implemented");
    }
}

