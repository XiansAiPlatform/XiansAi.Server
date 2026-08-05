using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers the configuration a tenant administrator is not allowed to save.
///
/// These rules exist so that a setting the validator would refuse or silently override at runtime
/// is reported while it is being saved, rather than surfacing later as users who cannot sign in.
/// </summary>
public class TenantOidcConfigServiceValidationTests
{
    private const string TenantId = "acme";

    private static TenantOidcConfigService CreateService(string environmentName = "Production")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:UniqueSecrets:TenantOidcSecretKey"] = "unit-test-secret"
            })
            .Build();

        var policy = new OidcValidationPolicy(
            configuration,
            Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environmentName),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            NullLogger<OidcValidationPolicy>.Instance);

        var encryption = new Mock<ISecureEncryptionService>();
        encryption.Setup(e => e.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string plaintext, string _) => plaintext);

        return new TenantOidcConfigService(
            Mock.Of<ITenantOidcConfigRepository>(),
            encryption.Object,
            NullLogger<TenantOidcConfigService>.Instance,
            configuration,
            new ObjectCache(Mock.Of<ICacheProvider>(), NullLogger<ObjectCache>.Instance),
            Mock.Of<IWebhookEventPublisher>(),
            policy);
    }

    private static string ConfigWith(string providerJson) =>
        $$"""
        {
          "tenantId": "{{TenantId}}",
          "providers": { "entra": {{providerJson}} }
        }
        """;

    [Fact]
    public async Task ProviderThatDisablesSignatureVerificationIsRejected()
    {
        // The validator ignores this setting, so accepting the save would leave an administrator
        // believing they had turned something off that is still on.
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "requireSignedTokens": false
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("requireSignedTokens", result.ErrorMessage);
    }

    [Theory]
    [InlineData("http://login.example.com")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://10.0.0.1")]
    [InlineData("not-a-url")]
    public async Task AuthorityTheServerMustNotFetchIsRejected(string authority)
    {
        var config = ConfigWith($$"""
            {
              "issuer": "{{authority}}",
              "authority": "{{authority}}",
              "expectedAudience": ["api"]
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("entra", result.ErrorMessage);
    }

    [Fact]
    public async Task LocalAuthorityIsAcceptedOutsideProduction()
    {
        var config = ConfigWith("""
            {
              "issuer": "http://localhost:8080/realms/xians",
              "authority": "http://localhost:8080/realms/xians",
              "expectedAudience": ["api"]
            }
            """);

        var result = await CreateService("Development").UpsertAsync(TenantId, config, "admin");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ProviderWithoutAnAudienceIsStillAccepted()
    {
        // Existing configurations were never required to declare an audience, so refusing here
        // would block those tenants from making unrelated edits. It is warned about instead, and
        // only becomes a hard failure once Auth:RequireOidcAudience is enabled.
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com"
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WellFormedProviderIsAccepted()
    {
        var config = ConfigWith("""
            {
              "issuer": "https://login.microsoftonline.com/abc/v2.0",
              "authority": "https://login.microsoftonline.com/abc/v2.0",
              "expectedAudience": ["api://xians"]
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.True(result.IsSuccess);
    }
}
