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
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers the configuration a SysAdmin is not allowed to save.
///
/// These rules exist so that a setting the validator would refuse or silently override at runtime
/// is reported while it is being saved, rather than surfacing later as users who cannot sign in.
/// </summary>
public class TenantOidcConfigServiceValidationTests
{
    private const string TenantId = "acme";

    private static TenantOidcConfigService CreateService(
        string environmentName = "Production",
        string? existingConfigJson = null)
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
        encryption.Setup(e => e.Decrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string ciphertext, string _) => ciphertext);

        var repository = new Mock<ITenantOidcConfigRepository>();
        if (existingConfigJson != null)
        {
            repository.Setup(r => r.GetByTenantIdAsync(TenantId))
                .ReturnsAsync(new TenantOidcConfig
                {
                    Id = "existing",
                    TenantId = TenantId,
                    EncryptedPayload = existingConfigJson,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "admin"
                });
        }

        return new TenantOidcConfigService(
            repository.Object,
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
    public async Task ProviderWithoutAnAudienceIsRejected()
    {
        // Without one the provider accepts anything its issuer signed, including a token minted for
        // an unrelated application there — and a UserApi sign-in turns a valid token into tenant
        // membership.
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com"
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("expectedAudience", result.ErrorMessage);
    }

    [Fact]
    public async Task ProviderWithAnEmptyAudienceListIsRejected()
    {
        // An empty list checks nothing, so it must not read as having declared an audience.
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": []
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("expectedAudience", result.ErrorMessage);
    }

    [Fact]
    public async Task PreExistingProviderWithoutAnAudienceIsStillRejected()
    {
        // Deliberately not grandfathered, unlike a mutable userIdClaim. Nothing else ever forces
        // these configurations to be revisited, so allowing unrelated edits to go through would
        // leave them audience-less forever. Sign-in keeps working; only saving is blocked.
        var existing = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com"
            }
            """);

        var result = await CreateService(existingConfigJson: existing)
            .UpsertAsync(TenantId, existing, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("expectedAudience", result.ErrorMessage);
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

    [Theory]
    [InlineData("email")]
    [InlineData("emails")]
    [InlineData("preferred_username")]
    public async Task NewlyIntroducedMutableUserIdClaimIsRejected(string claim)
    {
        // This is the parkly failure mode: nominating emails as the subject makes UserApi create
        // email-keyed accounts that never match the portal's GUID records.
        var config = ConfigWith($$"""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "{{claim}}" }
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("mutable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(claim, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StableUserIdClaimIsAccepted()
    {
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "sub" }
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CustomImmutableUserIdClaimIsAccepted()
    {
        // Unknown claims are allowed so a directory's genuine immutable custom claim is not blocked.
        var config = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "extension_ImmutableId" }
            }
            """);

        var result = await CreateService().UpsertAsync(TenantId, config, "admin");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UnchangedPreExistingMutableUserIdClaimIsGrandfathered()
    {
        // Without this, a tenant already on emails cannot edit any other setting without changing
        // userIdClaim — which would move every ParticipantId and strand conversation history.
        var existing = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "emails" }
            }
            """);

        var result = await CreateService(existingConfigJson: existing)
            .UpsertAsync(TenantId, existing, "admin");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ChangingAGrandfatheredMutableUserIdClaimIsRejected()
    {
        var existing = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "emails" }
            }
            """);

        var changed = ConfigWith("""
            {
              "issuer": "https://login.example.com",
              "authority": "https://login.example.com",
              "expectedAudience": ["api"],
              "providerSpecificSettings": { "userIdClaim": "email" }
            }
            """);

        var result = await CreateService(existingConfigJson: existing)
            .UpsertAsync(TenantId, changed, "admin");

        Assert.False(result.IsSuccess);
        Assert.Contains("mutable", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
