using Features.UserApi.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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

    private readonly Mock<IUserTenantService> _userTenantService = new();

    private AuthorizedTenantResolver BuildResolver(IMemoryCache? cache = null)
    {
        var configuration = new ConfigurationBuilder().Build();

        return new AuthorizedTenantResolver(
            _userTenantService.Object,
            cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            configuration,
            NullLogger<AuthorizedTenantResolver>.Instance);
    }

    private static OidcValidationResult ValidToken() =>
        OidcValidationResult.Ok(CanonicalUserId, ProviderUserId, "user@example.com", "Test User");

    private void SetupApprovedTenants(params string[] tenantIds)
    {
        var tenants = tenantIds.Select(t => new TenantInfoDto { TenantId = t, Name = t }).ToList();
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants(ProviderUserId, It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<List<TenantInfoDto>>.Success(tenants));
    }

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
        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenTokenCarriesNoProviderSubject()
    {
        var resolver = BuildResolver();
        var validation = OidcValidationResult.Ok(CanonicalUserId, string.Empty, null, null);

        var resolution = await resolver.ResolveAsync(validation, "tenant-a");

        Assert.False(resolution.IsAuthorized);
        _userTenantService.Verify(
            x => x.EnsureUserAndGetApprovedTenants(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Denies_WhenTenantLookupFails()
    {
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants(ProviderUserId, It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<List<TenantInfoDto>>.InternalServerError("boom"));
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
            x => x.EnsureUserAndGetApprovedTenants(ProviderUserId, It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotCacheFailures_SoATransientErrorDoesNotLockTheUserOut()
    {
        _userTenantService
            .SetupSequence(x => x.EnsureUserAndGetApprovedTenants(ProviderUserId, It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<List<TenantInfoDto>>.InternalServerError("transient"))
            .ReturnsAsync(ServiceResult<List<TenantInfoDto>>.Success(
                new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "tenant-a" } }));
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
        _userTenantService
            .Setup(x => x.EnsureUserAndGetApprovedTenants("other-subject", It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<List<TenantInfoDto>>.Success(new List<TenantInfoDto>()));
        var resolver = BuildResolver(cache);

        var approved = await resolver.ResolveAsync(ValidToken(), "tenant-a");
        var otherUser = await resolver.ResolveAsync(
            OidcValidationResult.Ok("keycloak|other-subject", "other-subject", null, null), "tenant-a");

        Assert.True(approved.IsAuthorized);
        Assert.False(otherUser.IsAuthorized);
    }
}
