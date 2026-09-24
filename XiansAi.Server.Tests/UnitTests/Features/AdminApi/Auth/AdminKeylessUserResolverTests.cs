using Features.AdminApi.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Exceptions;
using Shared.Repositories;
using Shared.Services;

namespace Tests.UnitTests.Features.AdminApi.Auth;

public class AdminKeylessUserResolverTests
{
    private const string ProviderUserId = "provider-subject-abc123";
    private const string Email = "user@example.com";
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private readonly Mock<IDynamicOidcValidator> _oidcValidator = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantCacheService> _tenantCache = new();

    public AdminKeylessUserResolverTests()
    {
        // AdminKeylessUserResolver derives roles from the already-loaded User via this pure method
        // rather than re-fetching
        _userRepo
            .Setup(x => x.GetUserRoles(It.IsAny<User>(), It.IsAny<string>()))
            .Returns((User user, string tenantId) =>
            {
                if (user.IsLockedOut) return new List<string>();
                var roles = user.TenantRoles
                    .FirstOrDefault(tr => tr.Tenant == tenantId && tr.IsApproved)?.Roles.ToList()
                    ?? new List<string>();
                if (user.IsSysAdmin) roles.Add(SystemRoles.SysAdmin);
                return roles;
            });
    }

    private AdminKeylessUserResolver BuildResolver() =>
        new(_oidcValidator.Object, _userRepo.Object, _tenantCache.Object,
            NullLogger<AdminKeylessUserResolver>.Instance);

    private void SetupValidToken(string? email = Email, bool emailVerified = true) =>
        _oidcValidator
            .Setup(x => x.ValidateAsync(AdminKeylessUserResolver.AdminConsolePseudoTenant, "raw-token"))
            .ReturnsAsync(OidcValidationResult.Ok(
                "provider|" + ProviderUserId, ProviderUserId, "https://login.example.com",
                email, "Test User", emailVerified: emailVerified));

    private static User MakeUser(
        string userId, bool isSysAdmin = false, bool isLockedOut = false,
        params (string Tenant, bool Approved, string[] Roles)[] tenantRoles) => new()
    {
        Id = userId,
        UserId = userId,
        Email = Email,
        IsSysAdmin = isSysAdmin,
        IsLockedOut = isLockedOut,
        TenantRoles = tenantRoles.Select(tr => new TenantRole
        {
            Tenant = tr.Tenant,
            IsApproved = tr.Approved,
            Roles = tr.Roles.ToList()
        }).ToList()
    };

    [Fact]
    public async Task ResolveAsync_FailsWithoutAnyLookup_WhenTokenValidationFails()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(AdminKeylessUserResolver.AdminConsolePseudoTenant, "raw-token"))
            .ReturnsAsync(OidcValidationResult.Fail("bad signature"));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("Invalid user token", result.ErrorMessage);
        _userRepo.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenTokenHasNoUsableSubject()
    {
        _oidcValidator
            .Setup(x => x.ValidateAsync(AdminKeylessUserResolver.AdminConsolePseudoTenant, "raw-token"))
            .ReturnsAsync(OidcValidationResult.Ok("provider|", "", null, Email, "Test User"));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("Invalid user token", result.ErrorMessage);
        _userRepo.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_NeverConsultsEmailFallback_WhenSubjectMatchesDirectly()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId))
            .ReturnsAsync(MakeUser(ProviderUserId, tenantRoles: (TenantA, true, new[] { "TenantUser" })));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.True(result.Success);
        Assert.Equal(TenantA, result.FinalTenantId);
        _userRepo.Verify(x => x.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToEmail_AndResolvesToThatUser_WhenExactlyOneMatch()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync((User?)null);
        var matched = MakeUser("canonical-user-id", tenantRoles: (TenantA, true, new[] { "TenantUser" }));
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(Email)).ReturnsAsync(new List<User> { matched });

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.True(result.Success);
        Assert.Equal("canonical-user-id", result.CanonicalUserId);
    }

    [Fact]
    public async Task ResolveAsync_RefusesEmailFallback_WhenMultipleAccountsShareTheEmail()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(Email)).ReturnsAsync(new List<User>
        {
            MakeUser("user-1"), MakeUser("user-2")
        });

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Contains("Multiple platform accounts", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenEmailFallbackFindsNoUsers()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(Email)).ReturnsAsync(new List<User>());

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("User is not registered on this platform", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_SkipsEmailLookupEntirely_WhenTokenCarriesNoEmail()
    {
        SetupValidToken(email: null);
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync((User?)null);

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("User is not registered on this platform", result.ErrorMessage);
        _userRepo.Verify(x => x.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotAttemptEmailFallback_WhenProviderDidNotAssertEmailVerified()
    {
        // Security: an unverified email is just whatever the caller typed into the provider's
        // signup form. Using it to authenticate as an existing account would let a misconfigured
        // or malicious provider registered for admin-console impersonate anyone by asserting a
        // matching (but unverified) address.
        SetupValidToken(emailVerified: false);
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync((User?)null);

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("User is not registered on this platform", result.ErrorMessage);
        _userRepo.Verify(x => x.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenNonSysAdminHasNoApprovedTenantMembership()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId,
            tenantRoles: [
                (TenantA, false, new[] { "TenantUser" }),      // not approved
                (TenantB, true, Array.Empty<string>())          // approved but no roles
            ]));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("User is not an approved member of any tenant", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_AutoResolves_WhenNonSysAdminHasExactlyOneApprovedTenant()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId, tenantRoles: (TenantA, true, new[] { "TenantUser" })));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.True(result.Success);
        Assert.Equal(TenantA, result.FinalTenantId);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenNonSysAdminHasMultipleTenants_AndNoneRequested()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId,
            tenantRoles: [
                (TenantA, true, new[] { "TenantUser" }),
                (TenantB, true, new[] { "TenantUser" })
            ]));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        Assert.False(result.Success);
        Assert.Equal("User is a member of multiple tenants; specify tenantId explicitly", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesToNamedTenant_WhenNonSysAdminDisambiguatesExplicitly()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId,
            tenantRoles: [
                (TenantA, true, new[] { "TenantUser" }),
                (TenantB, true, new[] { "TenantAdmin" })
            ]));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: TenantB);

        Assert.True(result.Success);
        Assert.Equal(TenantB, result.FinalTenantId);
        Assert.Equal(new[] { "TenantAdmin" }, result.UserRoles);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenRequestedTenantIsNotAnApprovedMembership()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId, tenantRoles: (TenantA, true, new[] { "TenantUser" })));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: TenantB);

        Assert.False(result.Success);
        Assert.Equal("Tenant ID does not match any tenant where the user is an approved member", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_SysAdmin_ResolvesToRequestedExistingTenant()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(ProviderUserId, isSysAdmin: true));
        _tenantCache.Setup(x => x.GetByTenantIdAsync(TenantA, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new Tenant
            {
                Id = "id1", TenantId = TenantA, Name = TenantA,
                CreatedAt = DateTime.UtcNow, CreatedBy = "test"
            });

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: TenantA);

        Assert.True(result.Success);
        Assert.Equal(TenantA, result.FinalTenantId);
        Assert.Contains(SystemRoles.SysAdmin, result.UserRoles!);
    }

    [Fact]
    public async Task ResolveAsync_SysAdmin_ThrowsTenantNotFound_ForANonExistentTenant()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(ProviderUserId, isSysAdmin: true));
        _tenantCache.Setup(x => x.GetByTenantIdAsync("ghost-tenant", It.IsAny<CancellationToken>(), false))
            .ReturnsAsync((Tenant?)null);

        await Assert.ThrowsAsync<TenantNotFoundException>(() =>
            BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: "ghost-tenant"));
    }

    [Fact]
    public async Task ResolveAsync_SysAdmin_Fails_WhenNoTenantRequestedAndTenantIsRequired()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(ProviderUserId, isSysAdmin: true));

        var result = await BuildResolver().ResolveAsync(
            "raw-token", tenantIdFromRequest: null, tenantRequiredForSysAdmin: true);

        Assert.False(result.Success);
        Assert.Contains("must specify a tenantId", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_SysAdmin_ResolvesToNonePlaceholder_WhenTenantOptional()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(ProviderUserId, isSysAdmin: true));

        var result = await BuildResolver().ResolveAsync(
            "raw-token", tenantIdFromRequest: null, tenantRequiredForSysAdmin: false);

        Assert.True(result.Success);
        Assert.Equal("none", result.FinalTenantId);
    }

    [Fact]
    public async Task ResolveAsync_SysAdmin_TakesTheSysAdminBranch_EvenWithExistingTenantMemberships()
    {
        // The SysAdmin branch must take priority over tenant-membership resolution: a SysAdmin who
        // also happens to hold an approved membership somewhere should still need to name a tenant
        // explicitly (or use a tenant-optional route), not be silently auto-resolved to their one
        // membership the way a non-SysAdmin would be.
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId, isSysAdmin: true, tenantRoles: (TenantA, true, new[] { "TenantUser" })));

        var result = await BuildResolver().ResolveAsync(
            "raw-token", tenantIdFromRequest: null, tenantRequiredForSysAdmin: true);

        Assert.False(result.Success);
        Assert.Contains("must specify a tenantId", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_DerivesRolesFromTheResolvedTenant_NotSomeOtherOne()
    {
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId,
            tenantRoles: [
                (TenantA, true, new[] { "TenantUser" }),
                (TenantB, true, new[] { "TenantAdmin" })
            ]));

        var result = await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: TenantA);

        Assert.Equal(new[] { "TenantUser" }, result.UserRoles);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReFetchTheUser_ToComputeRoles()
    {
        // Performance regression guard: roles must come from the User already loaded to resolve
        // identity/tenant, not a second GetByUserIdAsync/GetUserRolesAsync round trip.
        SetupValidToken();
        _userRepo.Setup(x => x.GetByUserIdAsync(ProviderUserId)).ReturnsAsync(MakeUser(
            ProviderUserId, tenantRoles: (TenantA, true, new[] { "TenantUser" })));

        await BuildResolver().ResolveAsync("raw-token", tenantIdFromRequest: null);

        _userRepo.Verify(x => x.GetByUserIdAsync(ProviderUserId), Times.Once);
        _userRepo.Verify(x => x.GetUserRolesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
