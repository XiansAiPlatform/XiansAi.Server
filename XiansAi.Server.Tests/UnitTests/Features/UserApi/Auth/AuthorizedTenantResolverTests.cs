using Features.UserApi.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Tests.UnitTests.Features.UserApi.Auth;

public class AuthorizedTenantResolverTests
{
    private const string ProviderUserId = "provider-subject-abc123";
    private const string CanonicalUserId = "keycloak|provider-subject-abc123";
    private const string ProviderAuthority = "https://login.example.com";

    private readonly Mock<IUserTenantService> _userTenantService = new();

    private AuthorizedTenantResolver BuildResolver(
        IMemoryCache? cache = null, IUserCacheIndex? userCacheIndex = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var memoryCache = cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });

        return new AuthorizedTenantResolver(
            _userTenantService.Object,
            memoryCache,
            userCacheIndex ?? new UserCacheIndex(memoryCache, NullLogger<UserCacheIndex>.Instance),
            configuration,
            BuildPolicy(),
            NullLogger<AuthorizedTenantResolver>.Instance);
    }

    private static OidcValidationPolicy BuildPolicy() =>
        new(
            new ConfigurationBuilder().Build(),
            Mock.Of<IHostEnvironment>(env => env.EnvironmentName == "Production"),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            NullLogger<OidcValidationPolicy>.Instance);

    /// <summary>A token checked against the audiences its tenant's provider declared.</summary>
    private static OidcValidationResult ValidToken() =>
        OidcValidationResult.Ok(
            CanonicalUserId, ProviderUserId, ProviderAuthority, "user@example.com", "Test User",
            audienceValidated: true);

    /// <summary>
    /// A token from a tenant whose provider declares no expectedAudience, so it was accepted on its
    /// issuer's signature alone and may have been minted for an unrelated application.
    /// </summary>
    private static OidcValidationResult TokenWithNoAudienceChecked() =>
        OidcValidationResult.Ok(
            CanonicalUserId, ProviderUserId, ProviderAuthority, "user@example.com", "Test User",
            audienceValidated: false);

    private void SetupApprovedTenants(params string[] tenantIds)
    {
        SetupApprovedTenantsFor(ProviderUserId, ProviderAuthority, tenantIds);
    }

    private void SetupApprovedTenantsFor(string providerUserId, string providerAuthority, params string[] tenantIds)
    {
        var tenants = tenantIds.Select(t => new TenantInfoDto { TenantId = t, Name = t }).ToList();
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(providerUserId, providerAuthority), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<ResolvedUserAccess>.Success(
                new ResolvedUserAccess { UserId = providerUserId, Tenants = tenants }));
    }

    private static SignInIdentity IdentityOf(string providerUserId, string providerAuthority) =>
        It.Is<SignInIdentity>(i => i.UserId == providerUserId && i.ProviderAuthority == providerAuthority);

    [Fact]
    public async Task ResolveAsync_Denies_WhenUserHasNoApprovedTenants()
    {
        SetupApprovedTenants();
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.False(resolution.IsAuthorized);
        Assert.Null(resolution.MatchedTenantId);
        Assert.Empty(resolution.AuthorizedTenantIds);
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenUserIsApprovedForADifferentTenant()
    {
        SetupApprovedTenants("tenant-b");
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.False(resolution.IsAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_Authorizes_WhenUserIsApprovedForTheRequestedTenant()
    {
        SetupApprovedTenants("tenant-a", "tenant-b");
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.True(resolution.IsAuthorized);
        Assert.Equal("tenant-a", resolution.MatchedTenantId);
        Assert.Equal(new[] { "tenant-a", "tenant-b" }, resolution.AuthorizedTenantIds);
    }

    [Fact]
    public async Task ResolveAsync_MatchesTenantIdCaseInsensitively_AndReturnsTheStoredCasing()
    {
        SetupApprovedTenants("Tenant-A");
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.True(resolution.IsAuthorized);
        Assert.Equal("Tenant-A", resolution.MatchedTenantId);
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenNoTenantIdWasRequested()
    {
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), string.Empty);

        Assert.False(resolution.IsAuthorized);
        VerifyNoLookup();
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenTokenCarriesNoProviderSubject()
    {
        var resolver = BuildResolver();
        var validation = OidcValidationResult.Ok(CanonicalUserId, string.Empty, ProviderAuthority, null, null);

        var resolution = await resolver.ResolveAsync(validation, "tenant-a");

        Assert.False(resolution.IsAuthorized);
        VerifyNoLookup();
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenTokenCarriesNoProviderAuthority()
    {
        // Without it the subject cannot be tied to one provider, so it cannot be trusted to identify
        // the stored user.
        var resolver = BuildResolver();
        var validation = OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, null, null, null);

        var resolution = await resolver.ResolveAsync(validation, "tenant-a");

        Assert.False(resolution.IsAuthorized);
        VerifyNoLookup();
    }

    [Fact]
    public async Task ResolveAsync_ReChecksTheAccount_AfterItsCachedAccessIsInvalidated()
    {
        // The cached entry holds the approved tenants, so a hit never reaches the lockout check.
        // Disabling the account has to drop the entry, or the account keeps its access for as long
        // as the entry lives.
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var index = new UserCacheIndex(cache, NullLogger<UserCacheIndex>.Instance);
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver(cache, index);
        await resolver.ResolveAsync(ValidToken(), "tenant-a");

        index.Invalidate(ProviderUserId);
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(ProviderUserId, ProviderAuthority), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<ResolvedUserAccess>.Forbidden("User account is disabled"));

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.False(resolution.IsAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenTenantLookupFails()
    {
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(ProviderUserId, ProviderAuthority), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<ResolvedUserAccess>.InternalServerError("boom"));
        var resolver = BuildResolver();

        var resolution = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.False(resolution.IsAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_LooksUpTheUserOnceAcrossRepeatedRequests()
    {
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver();

        await resolver.ResolveAsync(ValidToken(), "tenant-a");
        await resolver.ResolveAsync(ValidToken(), "tenant-a");

        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(ProviderUserId, ProviderAuthority), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_PassesTheRequestedTenantThrough_SoADeniedUserBecomesVisibleToItsAdmins()
    {
        // A user who is not a member is refused either way, but the tenant they were trying to reach
        // is what lets them be registered as pending there instead of vanishing.
        SetupApprovedTenants();
        var resolver = BuildResolver();

        await resolver.ResolveAsync(ValidToken(), "tenant-a");

        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(ProviderUserId, ProviderAuthority), "tenant-a", It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_PassesTheAddressAndNameOnForProvisioning()
    {
        // Whatever the provider says about the address is passed through, because a record with no
        // address cannot be matched to a person and nothing later fills it in. Azure B2C in
        // particular vouches for nothing, and its users still need an address on the record.
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver();
        var validation = OidcValidationResult.Ok(
            CanonicalUserId, ProviderUserId, ProviderAuthority, "user@example.com", "Test User");

        await resolver.ResolveAsync(validation, "tenant-a");

        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(
                It.Is<SignInIdentity>(i =>
                    i.Email == "user@example.com" && i.Name == "Test User"),
                "tenant-a", It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_AsksForTheMembershipToBeApproved()
    {
        // This token was checked against the audiences the tenant declared, so it was minted for
        // this tenant's own application — which is what makes holding one the tenant's statement
        // that this person belongs to it.
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver();

        await resolver.ResolveAsync(ValidToken(), "tenant-a");

        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(It.IsAny<SignInIdentity>(), "tenant-a", true),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotAskForApproval_WhenNoAudienceWasChecked()
    {
        // Without a declared audience the provider accepts anything its issuer signed, including a
        // token minted for an unrelated application there. That says nothing about this tenant, so
        // the membership goes back to waiting for an admin.
        SetupApprovedTenants("tenant-a");
        var resolver = BuildResolver();

        await resolver.ResolveAsync(TokenWithNoAudienceChecked(), "tenant-a");

        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(It.IsAny<SignInIdentity>(), "tenant-a", false),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotCacheFailures_SoATransientErrorDoesNotLockTheUserOut()
    {
        _userTenantService
            .SetupSequence(x => x.EnsureUserAndGetApprovedTenants(
                IdentityOf(ProviderUserId, ProviderAuthority), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<ResolvedUserAccess>.InternalServerError("transient"))
            .ReturnsAsync(ServiceResult<ResolvedUserAccess>.Success(new ResolvedUserAccess
            {
                UserId = ProviderUserId,
                Tenants = new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "tenant-a" } }
            }));
        var resolver = BuildResolver();

        var firstAttempt = await resolver.ResolveAsync(ValidToken(), "tenant-a");
        var secondAttempt = await resolver.ResolveAsync(ValidToken(), "tenant-a");

        Assert.False(firstAttempt.IsAuthorized);
        Assert.True(secondAttempt.IsAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_KeepsUsersApart_WhenTheyShareACache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        SetupApprovedTenants("tenant-a");
        SetupApprovedTenantsFor("other-subject", ProviderAuthority);
        var resolver = BuildResolver(cache);

        var approved = await resolver.ResolveAsync(ValidToken(), "tenant-a");
        var otherUser = await resolver.ResolveAsync(
            OidcValidationResult.Ok("keycloak|other-subject", "other-subject", ProviderAuthority, null, null),
            "tenant-a");

        Assert.True(approved.IsAuthorized);
        Assert.False(otherUser.IsAuthorized);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotServeOneProvidersCachedTenants_ToAnotherProviderWithTheSameSubject()
    {
        // A subject is only unique within an issuer, so the cache must not let a second provider
        // asserting the same subject skip the lookup that checks which provider the user belongs to.
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        SetupApprovedTenants("tenant-a");
        SetupApprovedTenantsFor(ProviderUserId, "https://evil.example");
        var resolver = BuildResolver(cache);

        var genuine = await resolver.ResolveAsync(ValidToken(), "tenant-a");
        var impostor = await resolver.ResolveAsync(
            OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "https://evil.example", null, null),
            "tenant-a");

        Assert.True(genuine.IsAuthorized);
        Assert.False(impostor.IsAuthorized);
    }

    private void VerifyNoLookup()
    {
        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(
                It.IsAny<SignInIdentity>(), It.IsAny<string?>(), It.IsAny<bool>()),
            Times.Never);
    }
}
