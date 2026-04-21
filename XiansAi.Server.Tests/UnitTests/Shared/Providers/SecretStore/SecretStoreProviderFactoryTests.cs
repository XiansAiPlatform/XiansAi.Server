using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Providers;
using Xunit;

namespace Tests.UnitTests.Shared.Providers.SecretStore;

public class SecretStoreProviderFactoryTests
{
    [Fact]
    public void DefaultsToDatabaseProviderWhenNotConfigured()
    {
        var services = new ServiceCollection();

        SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(new Dictionary<string, string?>()));

        Assert.Equal(typeof(DatabaseSecretStoreProvider), GetRegisteredImplementation(services));
    }

    [Theory]
    [InlineData("database")]
    [InlineData("Database")]
    [InlineData("DATABASE")]
    public void RegistersDatabaseProvider(string providerName)
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?> { ["SecretStore:Provider"] = providerName };

        SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(settings));

        Assert.Equal(typeof(DatabaseSecretStoreProvider), GetRegisteredImplementation(services));
    }

    [Theory]
    [InlineData("azurekeyvault")]
    [InlineData("AzureKeyVault")]
    public void RegistersAzureKeyVaultProvider(string providerName)
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?>
        {
            ["SecretStore:Provider"] = providerName,
            ["SecretStore:AzureKeyVault:VaultUri"] = "https://example-vault.vault.azure.net/"
        };

        SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(settings));

        // Azure provider is registered with a factory delegate, so the descriptor's ImplementationType is null.
        var descriptor = GetSecretStoreDescriptor(services);
        Assert.Null(descriptor.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);

        // SecretClient should also be registered as a singleton.
        Assert.Contains(services, s => s.ServiceType == typeof(global::Azure.Security.KeyVault.Secrets.SecretClient));
    }

    [Fact]
    public void AzureKeyVaultProviderRequiresVaultUri()
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?> { ["SecretStore:Provider"] = "azurekeyvault" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(settings)));

        Assert.Contains("VaultUri", ex.Message);
    }

    [Fact]
    public void DoubleUnderscoreFormatIsSupported()
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?>
        {
            ["SecretStore__Provider"] = "azurekeyvault",
            ["SecretStore__AzureKeyVault__VaultUri"] = "https://example-vault.vault.azure.net/"
        };

        SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(settings));

        var descriptor = GetSecretStoreDescriptor(services);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    [Fact]
    public void UnknownProviderThrows()
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?> { ["SecretStore:Provider"] = "vault-2000" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SecretStoreProviderFactory.RegisterProvider(services, BuildConfig(settings)));

        Assert.Contains("vault-2000", ex.Message);
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Type? GetRegisteredImplementation(IServiceCollection services)
        => GetSecretStoreDescriptor(services).ImplementationType;

    private static ServiceDescriptor GetSecretStoreDescriptor(IServiceCollection services)
        => services.Single(s => s.ServiceType == typeof(ISecretStoreProvider));
}
