using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Providers;
using Xunit;

namespace Tests.UnitTests.Shared.Providers.SecretStore;

public class AzureKeyVaultSecretStoreProviderTests
{
    private const string SecretId = "507f1f77bcf86cd799439011";
    private const string Prefix = "xians-";
    private const string ExpectedName = "xians-507f1f77bcf86cd799439011";

    [Fact]
    public async Task SetAsync_CallsClientWithPrefixedName()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.SetSecretAsync(ExpectedName, "the-value", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSecretResponse(ExpectedName, "the-value"))
            .Verifiable();

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        await provider.SetAsync(SecretId, "the-value");

        client.Verify();
    }

    [Fact]
    public async Task GetAsync_ReturnsValue()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.GetSecretAsync(ExpectedName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSecretResponse(ExpectedName, "the-value"));

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        var result = await provider.GetAsync(SecretId);

        Assert.Equal("the-value", result);
    }

    [Fact]
    public async Task GetAsync_Returns_Null_When_NotFound()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.GetSecretAsync(ExpectedName, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        var result = await provider.GetAsync(SecretId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_PropagatesNon404Failures()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.GetSecretAsync(ExpectedName, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "boom"));

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        await Assert.ThrowsAsync<RequestFailedException>(() => provider.GetAsync(SecretId));
    }

    [Fact]
    public async Task DeleteAsync_CallsStartDelete()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.StartDeleteSecretAsync(ExpectedName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<DeleteSecretOperation>())
            .Verifiable();

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        await provider.DeleteAsync(SecretId);

        client.Verify();
    }

    [Fact]
    public async Task DeleteAsync_Treats_404_As_Success()
    {
        var client = new Mock<SecretClient>();
        client
            .Setup(c => c.StartDeleteSecretAsync(ExpectedName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));

        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        await provider.DeleteAsync(SecretId);
    }

    [Fact]
    public async Task SetAsync_RejectsInvalidVaultName()
    {
        var client = new Mock<SecretClient>(MockBehavior.Strict);
        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, "bad_prefix_", NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetAsync(SecretId, "value"));
    }

    [Fact]
    public void Name_Is_Stable()
    {
        var client = new Mock<SecretClient>();
        var provider = new AzureKeyVaultSecretStoreProvider(client.Object, Prefix, NullLogger<AzureKeyVaultSecretStoreProvider>.Instance);
        Assert.Equal("azurekeyvault", provider.Name);
    }

    private static Response<KeyVaultSecret> BuildSecretResponse(string name, string value)
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties(name),
            value);
        return Response.FromValue(secret, Mock.Of<Response>());
    }
}
