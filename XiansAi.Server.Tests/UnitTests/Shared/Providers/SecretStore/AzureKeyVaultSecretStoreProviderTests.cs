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

    private static Response<KeyVaultSecret> BuildSecretResponse(string name, string value)
    {
        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties(name),
            value);
        return Response.FromValue(secret, Mock.Of<Response>());
    }
}
