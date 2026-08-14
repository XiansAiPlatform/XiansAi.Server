using Features.UserApi.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Services;
using System.Security.Claims;

namespace Tests.UnitTests.Features.UserApi.Auth;

public class UserApiCredentialAuthenticatorTests
{
    private const string SchemeName = "EndpointApiKeyScheme";
    private const string RawApiKey = "sk-Xnai-abcdef0123456789";
    private const string Jwt = "header.payload.signature";
    private const string ProviderUserId = "provider-subject-abc123";
    private const string CanonicalUserId = "keycloak|provider-subject-abc123";

    private readonly Mock<IApiKeyService> _apiKeyService = new();
    private readonly Mock<IDynamicOidcValidator> _oidcValidator = new();
    private readonly Mock<IAuthorizedTenantResolver> _tenantResolver = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    public UserApiCredentialAuthenticatorTests()
    {
        _tenantContext.SetupProperty(x => x.LoggedInUser, string.Empty);
        _tenantContext.SetupProperty(x => x.TenantId, string.Empty);
        _tenantContext.SetupProperty(x => x.ParticipantId, string.Empty);
        _tenantContext.SetupProperty(x => x.Email, (string?)null);
        _tenantContext.SetupProperty(x => x.ProviderSubject, (string?)null);
        _tenantContext.SetupProperty(x => x.UserType, UserType.Unknown);
        _tenantContext.SetupProperty(x => x.Authorization, (string?)null);
        _tenantContext.SetupProperty<IEnumerable<string>>(x => x.AuthorizedTenantIds, Array.Empty<string>());
    }

    private UserApiCredentialAuthenticator BuildAuthenticator() =>
        new(_tenantContext.Object,
            _apiKeyService.Object,
            _oidcValidator.Object,
            _tenantResolver.Object,
            NullLogger<UserApiCredentialAuthenticator>.Instance);

    private static PresentedCredential FromHeader(string token) =>
        new(token, CredentialSource.AuthorizationHeader);

    private static PresentedCredential FromQuery(string token) =>
        new(token, CredentialSource.ApiKeyQueryParameter);

    private static ApiKey ApiKeyFor(string tenantId, string createdBy = "creator@example.com") =>
        new()
        {
            Id = "65f0000000000000000000aa",
            TenantId = tenantId,
            Name = "test-key",
            HashedKey = "hashed",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

    private void SetupValidJwt()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(It.IsAny<string>(), Jwt))
            .ReturnsAsync(OidcValidationResult.Ok(
                CanonicalUserId, ProviderUserId, "https://login.example.com", "user@example.com", "Test User"));
    }

    [Fact]
    public async Task ApiKeyWithoutTenant_DerivesTheTenantFromTheKey()
    {
        _apiKeyService.Setup(x => x.GetApiKeyByRawKeyAsync(RawApiKey)).ReturnsAsync(ApiKeyFor("tenant-a"));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(RawApiKey), null, SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal("tenant-a", result.Principal!.FindFirst(UserApiClaimTypes.TenantId)?.Value);
        Assert.Equal("creator@example.com", result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(nameof(UserType.UserApiKey), result.Principal.FindFirst(UserApiClaimTypes.UserType)?.Value);
        Assert.Equal(UserType.UserApiKey, _tenantContext.Object.UserType);
        Assert.Equal("tenant-a", _tenantContext.Object.TenantId);
    }

    [Fact]
    public async Task ApiKeyWithTenant_IsRejected_WhenTheKeyBelongsToAnotherTenant()
    {
        _apiKeyService
            .Setup(x => x.GetApiKeyByRawKeyAsync(RawApiKey, "tenant-b"))
            .ReturnsAsync(ApiKeyFor("tenant-a"));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(RawApiKey), "tenant-b", SchemeName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnknownApiKey_IsRejected()
    {
        _apiKeyService.Setup(x => x.GetApiKeyByRawKeyAsync(RawApiKey)).ReturnsAsync((ApiKey?)null);

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(RawApiKey), null, SchemeName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Jwt_IsRejected_WhenNoTenantIsSupplied()
    {
        // A JWT carries no tenant of its own, so there is nothing to select OIDC rules with.
        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), null, SchemeName);

        Assert.False(result.Succeeded);
        _oidcValidator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Jwt_IsRejected_WhenTheTenantIsNotOneTheUserBelongsTo()
    {
        SetupValidJwt();
        _tenantResolver
            .Setup(x => x.ResolveAsync(It.IsAny<OidcValidationResult>(), "tenant-a"))
            .ReturnsAsync(AuthorizedTenantResolution.Denied());

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), "tenant-a", SchemeName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Jwt_CarriesTheResolvedTenantAndParticipant_WhenAuthorized()
    {
        SetupValidJwt();
        _tenantResolver
            .Setup(x => x.ResolveAsync(It.IsAny<OidcValidationResult>(), "Tenant-A"))
            .ReturnsAsync(AuthorizedTenantResolution.Authorized("tenant-a", ["tenant-a", "tenant-b"], ProviderUserId));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), "Tenant-A", SchemeName);

        Assert.True(result.Succeeded);

        // The stored casing wins over whatever the caller sent.
        Assert.Equal("tenant-a", result.Principal!.FindFirst(UserApiClaimTypes.TenantId)?.Value);
        Assert.Equal(CanonicalUserId, result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(ProviderUserId, result.Principal.FindFirst(UserApiClaimTypes.ParticipantId)?.Value);
        Assert.Equal(ProviderUserId, result.Principal.FindFirst(UserApiClaimTypes.ProviderSubject)?.Value);
        Assert.Null(result.Principal.FindFirst(UserApiClaimTypes.Email));
        Assert.Equal(nameof(UserType.UserToken), result.Principal.FindFirst(UserApiClaimTypes.UserType)?.Value);
        Assert.Equal(
            new[] { "tenant-a", "tenant-b" },
            result.Principal.FindAll(UserApiClaimTypes.AuthorizedTenantId).Select(c => c.Value));
    }

    [Fact]
    public async Task Jwt_PrefersAccountEmailAsParticipantId_WhenTheResolvedAccountHasOne()
    {
        const string accountEmail = "user@example.com";

        SetupValidJwt();
        _tenantResolver
            .Setup(x => x.ResolveAsync(It.IsAny<OidcValidationResult>(), "tenant-a"))
            .ReturnsAsync(AuthorizedTenantResolution.Authorized(
                "tenant-a", ["tenant-a"], ProviderUserId, accountEmail, accountEmail));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), "tenant-a", SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal(accountEmail, result.Principal!.FindFirst(UserApiClaimTypes.ParticipantId)?.Value);
        Assert.Equal(accountEmail, result.Principal.FindFirst(UserApiClaimTypes.Email)?.Value);
        Assert.Equal(ProviderUserId, result.Principal.FindFirst(UserApiClaimTypes.ProviderSubject)?.Value);
        Assert.Equal(accountEmail, _tenantContext.Object.ParticipantId);
        Assert.Equal(accountEmail, _tenantContext.Object.Email);
        Assert.Equal(ProviderUserId, _tenantContext.Object.ProviderSubject);
    }

    [Fact]
    public async Task Jwt_NamesTheCallerByTheirSubject_ButStillReportsAWithheldSharedEmail()
    {
        // Another account holds the address, so it cannot namespace threads. It is still carried as
        // the account email so a client that keeps sending it is recognised rather than refused.
        const string sharedEmail = "shared@example.com";

        SetupValidJwt();
        _tenantResolver
            .Setup(x => x.ResolveAsync(It.IsAny<OidcValidationResult>(), "tenant-a"))
            .ReturnsAsync(AuthorizedTenantResolution.Authorized(
                "tenant-a", ["tenant-a"], ProviderUserId, conversationEmail: null, accountEmail: sharedEmail));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), "tenant-a", SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal(ProviderUserId, result.Principal!.FindFirst(UserApiClaimTypes.ParticipantId)?.Value);
        Assert.Equal(sharedEmail, result.Principal.FindFirst(UserApiClaimTypes.Email)?.Value);
        Assert.Equal(ProviderUserId, _tenantContext.Object.ParticipantId);
        Assert.Equal(sharedEmail, _tenantContext.Object.Email);
    }

    [Fact]
    public async Task FailureReasonIsGeneric_SoThatItRevealsNothingAboutTheTenantConfiguration()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync("tenant-a", Jwt))
            .ReturnsAsync(OidcValidationResult.Fail("No OIDC providers configured for tenant"));

        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader(Jwt), "tenant-a", SchemeName);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("OIDC", result.Failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CredentialIsForwardedDownstream_OnlyWhenItCameFromTheAuthorizationHeader()
    {
        _apiKeyService.Setup(x => x.GetApiKeyByRawKeyAsync(RawApiKey)).ReturnsAsync(ApiKeyFor("tenant-a"));
        var authenticator = BuildAuthenticator();

        await authenticator.AuthenticateAsync(FromQuery(RawApiKey), null, SchemeName);
        Assert.Null(_tenantContext.Object.Authorization);

        await authenticator.AuthenticateAsync(FromHeader(RawApiKey), null, SchemeName);
        Assert.Equal(RawApiKey, _tenantContext.Object.Authorization);
    }

    [Fact]
    public async Task CredentialThatIsNeitherApiKeyNorJwt_IsRejectedWithoutAnyLookup()
    {
        var result = await BuildAuthenticator().AuthenticateAsync(FromHeader("not-a-token"), "tenant-a", SchemeName);

        Assert.False(result.Succeeded);
        _apiKeyService.Verify(x => x.GetApiKeyByRawKeyAsync(It.IsAny<string>()), Times.Never);
        _oidcValidator.Verify(x => x.ValidateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApiKeyId_AuthenticatesTheKeysOwner()
    {
        _apiKeyService.Setup(x => x.GetApiKeyByIdAsync("65f0000000000000000000aa")).ReturnsAsync(ApiKeyFor("tenant-a"));

        var result = await BuildAuthenticator().AuthenticateByApiKeyIdAsync("65f0000000000000000000aa", SchemeName);

        Assert.True(result.Succeeded);
        Assert.Equal("tenant-a", result.Principal!.FindFirst(UserApiClaimTypes.TenantId)?.Value);
    }

    [Fact]
    public async Task UnknownApiKeyId_IsRejected()
    {
        _apiKeyService.Setup(x => x.GetApiKeyByIdAsync(It.IsAny<string>())).ReturnsAsync((ApiKey?)null);

        var result = await BuildAuthenticator().AuthenticateByApiKeyIdAsync("65f0000000000000000000aa", SchemeName);

        Assert.False(result.Succeeded);
    }
}
