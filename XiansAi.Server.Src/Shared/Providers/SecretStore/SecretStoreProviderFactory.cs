namespace Shared.Providers;

/// <summary>
/// Registers the active <see cref="ISecretStoreProvider"/> implementation in DI based on configuration.
///
/// Configuration keys:
/// <list type="bullet">
///   <item><c>SecretStore:Provider</c> — <c>database</c> (default) or <c>azurekeyvault</c>.</item>
///   <item><c>SecretStore:AzureKeyVault:VaultUri</c> — required when provider is <c>azurekeyvault</c>.</item>
///   <item><c>SecretStore:AzureKeyVault:SecretNamePrefix</c> — optional, defaults to <c>xians-</c>.</item>
/// </list>
/// Both colon (<c>SecretStore:Provider</c>) and double-underscore (<c>SecretStore__Provider</c>)
/// formats are supported, mirroring the cache provider factory.
/// </summary>
public class SecretStoreProviderFactory
{
    public const string DatabaseProviderName = "database";
    public const string AzureKeyVaultProviderName = "azurekeyvault";

    private static string? GetConfigValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = configuration[key.Replace(":", "__")];
        }
        return value;
    }

    /// <summary>
    /// Registers exactly one <see cref="ISecretStoreProvider"/> implementation based on
    /// <c>SecretStore:Provider</c>. Throws when an unknown provider is configured or required
    /// settings for the chosen provider are missing.
    /// </summary>
    public static void RegisterProvider(IServiceCollection services, IConfiguration configuration)
    {
        var providerName = GetConfigValue(configuration, "SecretStore:Provider");
        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = DatabaseProviderName;
        }

        switch (providerName.ToLowerInvariant())
        {
            case DatabaseProviderName:
                services.AddScoped<ISecretStoreProvider, DatabaseSecretStoreProvider>();
                break;

            case AzureKeyVaultProviderName:
                var vaultUri = GetConfigValue(configuration, "SecretStore:AzureKeyVault:VaultUri");
                if (string.IsNullOrWhiteSpace(vaultUri))
                {
                    throw new InvalidOperationException(
                        "Azure Key Vault secret store requires SecretStore:AzureKeyVault:VaultUri (or SecretStore__AzureKeyVault__VaultUri).");
                }
                var prefix = GetConfigValue(configuration, "SecretStore:AzureKeyVault:SecretNamePrefix")
                    ?? AzureKeyVaultSecretStoreProvider.DefaultSecretNamePrefix;

                services.AddSingleton(_ => AzureKeyVaultSecretStoreProvider.CreateDefaultClient(vaultUri));
                services.AddScoped<ISecretStoreProvider>(sp =>
                    new AzureKeyVaultSecretStoreProvider(
                        sp.GetRequiredService<global::Azure.Security.KeyVault.Secrets.SecretClient>(),
                        prefix,
                        sp.GetRequiredService<ILogger<AzureKeyVaultSecretStoreProvider>>()));
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported SecretStore:Provider '{providerName}'. Supported: '{DatabaseProviderName}', '{AzureKeyVaultProviderName}'.");
        }
    }
}
