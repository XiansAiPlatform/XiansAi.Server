using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// GetForTenantAsync always tries to cache what it fetches, but a no-op ICacheProvider reports
/// every SetAsync as unsuccessful. These cover that the service still returns the correct result
/// on both the null-config and found-config paths even though the "cached" branch is never taken.
/// </summary>
public class TenantOidcConfigServiceCachingTests
{
    private const string TenantId = "acme";

    private static TenantOidcConfigService CreateService(ITenantOidcConfigRepository repository)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:UniqueSecrets:TenantOidcSecretKey"] = "unit-test-secret"
            })
            .Build();

        var policy = new OidcValidationPolicy(
            configuration,
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == "Production"),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            NullLogger<OidcValidationPolicy>.Instance);

        var encryption = new Mock<ISecureEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string ciphertext, string _) => ciphertext);

        // The real no-op provider, not a mock: SetAsync genuinely always returns false here,
        // which is exactly the condition the reviewed branch has to handle correctly.
        return new TenantOidcConfigService(
            repository,
            encryption.Object,
            NullLogger<TenantOidcConfigService>.Instance,
            configuration,
            new ObjectCache(new NoOpCacheProvider(), NullLogger<ObjectCache>.Instance),
            Mock.Of<IWebhookEventPublisher>(),
            policy);
    }

    [Fact]
    public async Task GetForTenantAsync_ReturnsNull_WhenNoConfigExists_AndCacheProviderNeverStores()
    {
        var repository = new Mock<ITenantOidcConfigRepository>();
        repository.Setup(r => r.GetByTenantIdAsync(TenantId)).ReturnsAsync((TenantOidcConfig?)null);
        var service = CreateService(repository.Object);

        var result = await service.GetForTenantAsync(TenantId);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetForTenantAsync_ReturnsConfig_WhenCacheProviderNeverStores()
    {
        var configJson = """{"tenantId":"acme","allowedProviders":["google"]}""";
        var repository = new Mock<ITenantOidcConfigRepository>();
        repository.Setup(r => r.GetByTenantIdAsync(TenantId)).ReturnsAsync(new TenantOidcConfig
        {
            Id = "existing",
            TenantId = TenantId,
            EncryptedPayload = configJson,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin"
        });
        var service = CreateService(repository.Object);

        var result = await service.GetForTenantAsync(TenantId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(new[] { "google" }, result.Data!.AllowedProviders);
    }
}
