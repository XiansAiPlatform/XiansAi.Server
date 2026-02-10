using Microsoft.Extensions.Configuration;

namespace Shared.Providers.Secret;

/// <summary>
/// Factory for creating secret vault provider instances based on configuration.
/// </summary>
public static class SecretVaultProviderFactory
{
    public static ISecretVaultProvider CreateProvider(
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        var provider = configuration["SecretVault:Provider"]?.ToLowerInvariant() ?? "database";

        return provider switch
        {
            "database" => serviceProvider.GetRequiredService<DatabaseSecretVaultProvider>(),
            "azure" => serviceProvider.GetRequiredService<AzureKeyVaultProvider>(),
            "aws" => serviceProvider.GetRequiredService<AwsSecretsManagerProvider>(),
            "hashicorp" => serviceProvider.GetRequiredService<HashiCorpVaultProvider>(),
            _ => throw new InvalidOperationException($"Unknown secret vault provider: {provider}. Supported providers: database, azure, aws, hashicorp")
        };
    }
}

