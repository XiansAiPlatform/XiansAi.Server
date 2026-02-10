using Shared.Data.Models;
using System.Text.Json;

namespace Shared.Providers.Secret;

/// <summary>
/// Azure Key Vault provider implementation.
/// Stores the entire SecretData object as serialized JSON in Azure Key Vault.
/// </summary>
public class AzureKeyVaultProvider : ISecretVaultProvider
{
    private readonly ILogger<AzureKeyVaultProvider> _logger;

    public AzureKeyVaultProvider(ILogger<AzureKeyVaultProvider> logger)
    {
        _logger = logger;
    }

    public Task<SecretData?> GetSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    public Task<(List<SecretData> items, int totalCount)> ListSecretsAsync(
        string? tenantId, 
        string? agentId, 
        string? userId, 
        string? secretIdPattern, 
        int page, 
        int pageSize)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    public Task<bool> SecretExistsAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    public Task CreateSecretAsync(SecretData secret)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    public Task UpdateSecretAsync(string secretId, string? tenantId, string? agentId, string? userId, SecretData updates)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    public Task<bool> DeleteSecretAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        throw new NotImplementedException("Azure Key Vault provider is not yet implemented");
    }

    /// <summary>
    /// Generates the Azure Key Vault secret name from scopes.
    /// Format: {secretId}--{tenantId}--{agentId}--{userId}
    /// </summary>
    private static string GenerateSecretName(string secretId, string? tenantId, string? agentId, string? userId)
    {
        var normalizedTenantId = NormalizeScope(tenantId, "system");
        var normalizedAgentId = NormalizeScope(agentId, "global");
        var normalizedUserId = NormalizeScope(userId, "global");
        
        return $"{NormalizeSecretId(secretId)}--{normalizedTenantId}--{normalizedAgentId}--{normalizedUserId}";
    }

    private static string NormalizeScope(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        
        return NormalizeSecretId(value);
    }

    private static string NormalizeSecretId(string secretId)
    {
        // Replace unsupported characters with '-' and convert to lowercase
        // Allowed: [A-Za-z0-9-]
        var normalized = System.Text.RegularExpressions.Regex.Replace(secretId, @"[^A-Za-z0-9-]", "-");
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"-+", "-"); // Collapse multiple dashes
        return normalized.ToLowerInvariant().Trim('-');
    }
}

