using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace Shared.Providers;

/// <summary>
/// Secret Store provider backed by Azure Key Vault.
///
/// Storage model: each secret value is stored as a single Key Vault secret with name
/// <c>{prefix}{secretId}</c> (default prefix <c>"xians-"</c>). The secret value in Key Vault
/// holds the plaintext; transport is TLS and at-rest encryption is handled by Azure.
///
/// Auth: uses <c>DefaultAzureCredential</c> by default — managed identity in production,
/// developer credentials (Azure CLI / env vars) locally. The configured identity must have
/// at least <c>Get</c>, <c>Set</c>, and <c>Delete</c> permissions on the vault's secrets
/// (RBAC role <c>Key Vault Secrets Officer</c>).
///
/// Soft-delete &amp; purge-protection: when soft-delete is enabled on the vault (default for new vaults),
/// <see cref="DeleteAsync"/> only marks the secret deleted. Re-creating a key with the same name
/// while the previous version is still in the soft-deleted state will fail unless the deleted
/// secret is purged first. This is an Azure Key Vault behaviour — operators should consider
/// the vault's purge-protection / retention policy when planning rotation.
/// </summary>
public class AzureKeyVaultSecretStoreProvider : ISecretStoreProvider
{
    public const string DefaultSecretNamePrefix = "xians-";

    // Key Vault secret names are restricted to alphanumerics and dashes, max 127 chars.
    private static readonly Regex VaultNameRegex = new("^[0-9a-zA-Z-]{1,127}$", RegexOptions.Compiled);

    private readonly SecretClient _client;
    private readonly string _prefix;
    private readonly ILogger<AzureKeyVaultSecretStoreProvider> _logger;

    public AzureKeyVaultSecretStoreProvider(
        SecretClient client,
        string secretNamePrefix,
        ILogger<AzureKeyVaultSecretStoreProvider> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _prefix = string.IsNullOrEmpty(secretNamePrefix) ? DefaultSecretNamePrefix : secretNamePrefix;
        _logger = logger;
    }

    /// <summary>
    /// Constructs a default <see cref="SecretClient"/> against <paramref name="vaultUri"/> using
    /// <c>DefaultAzureCredential</c>.
    /// </summary>
    public static SecretClient CreateDefaultClient(string vaultUri, TokenCredential? credential = null)
    {
        if (string.IsNullOrWhiteSpace(vaultUri))
            throw new ArgumentException("vaultUri must be provided", nameof(vaultUri));

        return new SecretClient(new Uri(vaultUri), credential ?? new global::Azure.Identity.DefaultAzureCredential());
    }

    public string Name => "azurekeyvault";

    public async Task SetAsync(string secretId, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));
        ArgumentNullException.ThrowIfNull(value);

        var name = ToVaultName(secretId);
        try
        {
            await _client.SetSecretAsync(name, value, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure Key Vault SetSecret failed for secret id {SecretId} (status {Status})",
                secretId, ex.Status);
            throw;
        }
    }

    public async Task<string?> GetAsync(string secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));

        var name = ToVaultName(secretId);
        try
        {
            var response = await _client.GetSecretAsync(name, cancellationToken: cancellationToken);
            return response.Value?.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure Key Vault GetSecret failed for secret id {SecretId} (status {Status})",
                secretId, ex.Status);
            throw;
        }
    }

    public async Task DeleteAsync(string secretId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretId))
            throw new ArgumentException("secretId must be provided", nameof(secretId));

        var name = ToVaultName(secretId);
        try
        {
            await _client.StartDeleteSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Already gone; treat as success.
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Azure Key Vault StartDeleteSecret failed for secret id {SecretId} (status {Status})",
                secretId, ex.Status);
            throw;
        }
    }

    private string ToVaultName(string secretId)
    {
        var name = _prefix + secretId;
        if (!VaultNameRegex.IsMatch(name))
        {
            throw new InvalidOperationException(
                $"Resulting Key Vault secret name is invalid (must match {VaultNameRegex}). " +
                "Check the SecretNamePrefix setting and the generated secret id.");
        }
        return name;
    }
}
