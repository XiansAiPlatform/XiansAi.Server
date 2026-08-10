using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class UserTenantServiceApprovedTenantsTests
{
    private const string UserId = "provider-subject-abc123";
    private const string Authority = "https://login.example.com";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IAuthMgtConnect> _authMgtConnect = new();
    private readonly Mock<IUserManagementService> _userManagementService = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IJwtClaimsExtractor> _jwtExtractor = new();

    private UserTenantService BuildService() =>
        new(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            _authMgtConnect.Object,
            new ConfigurationBuilder().Build(),
            _userManagementService.Object,
            _tenantRepo.Object,
            _jwtExtractor.Object);

    private static Tenant BuildTenant(string id, string tenantId, string name, bool enabled) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Enabled = enabled,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };

    /// <summary>Lets an unpinned record adopt whichever authority is presented.</summary>
    private void AllowPinAdoption()
    {
        _userRepo
            .Setup(x => x.PinProviderAuthorityIfUnsetAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync((string _, string authority) => authority);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsAnEmptyUserId()
    {
        var result = await BuildService().EnsureUserAndGetApprovedTenants(string.Empty, null, null, Authority);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsATokenWithNoProviderAuthority()
    {
        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ReturnsNoTenants_ForAFirstTimeUser()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.Tenants);
    }

    /// <summary>
    /// A first-time user arriving with a tenant id, where that tenant exists and is enabled.
    /// </summary>
    private void ArrangeFirstTimeUserRequesting(string tenantId, bool tenantEnabled = true)
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync(tenantId))
            .ReturnsAsync(BuildTenant("id-1", tenantId, "Tenant", tenantEnabled));
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersAFirstTimeUserAsPendingOnTheTenantTheyAskedFor()
    {
        // Without this the user is provisioned but belongs to nothing, and the tenant's own admins
        // cannot see them at all — both of their listings match on a TenantRoles entry.
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(UserId, "acme"), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PendingMembershipGrantsNoAccess()
    {
        ArrangeFirstTimeUserRequesting("acme");

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        // Being registered as pending must not put the tenant in the approved list, or the caller
        // would let them straight in.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.Tenants);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_UsesTheStoredCasingOfTheTenantId()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("ACME"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "ACME");

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(UserId, "acme"), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingWhenNoTenantWasRequested(string? tenantId)
    {
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, tenantId);

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingForATenantThatDoesNotExist()
    {
        // The tenant id comes from the caller, so without this check anyone with a valid token could
        // append a row to their own record for every name they cared to try.
        ArrangeFirstTimeUserRequesting("acme");
        _tenantRepo.Setup(x => x.GetByTenantIdAsync("does-not-exist")).ReturnsAsync((Tenant?)null);

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "does-not-exist");

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingForADisabledTenant()
    {
        ArrangeFirstTimeUserRequesting("acme", tenantEnabled: false);

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersAnExistingUserReachingATenantTheyDoNotBelongTo()
    {
        // A known user meeting a new tenant is the same situation as a brand new one.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            Email = "user@example.com",
            Name = "Test User",
            ProviderAuthority = Authority,
            TenantRoles = new List<TenantRole>()
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));

        await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(UserId, "acme"), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_StillReturnsTenantsWhenRecordingThePendingMembershipFails()
    {
        // Visibility for admins is a convenience. Losing it must not turn an ordinary sign-in into
        // an error for a user who is already approved elsewhere.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            TenantRoles = new List<TenantRole>()
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "other", Name = "Other" } });
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));
        _userRepo
            .Setup(x => x.AddPendingTenantRoleIfAbsentAsync(UserId, "acme"))
            .ThrowsAsync(new Exception("mongo unavailable"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        Assert.True(result.IsSuccess);
        Assert.Equal("other", Assert.Single(result.Data!.Tenants).TenantId);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RefusesASubjectWhoseEmailBelongsToAnotherAccount()
    {
        // The token proves the provider says this person's email is that string. It does not prove
        // they are the account already holding it, and that account may carry far more access —
        // so this is refused, never merged.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("A user with this email already exists"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "taken@example.com", "Test User", Authority, "acme");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.AddPendingTenantRoleIfAbsentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_StillHandlesALostRaceToCreateTheSameSubject()
    {
        // Same Conflict from the creator, but here the record really is this subject's, so the
        // sign-in continues rather than being mistaken for an email collision.
        var racedUser = new User { UserId = UserId, ProviderAuthority = Authority, Email = "user@example.com" };
        _userRepo.SetupSequence(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync((User?)null)
            .ReturnsAsync(racedUser)
            .ReturnsAsync(racedUser);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "acme", Name = "Acme" } });
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("User already exists"));
        _tenantRepo.Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Acme", true));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, "user@example.com", "Test User", Authority, "acme");

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", Assert.Single(result.Data!.Tenants).TenantId);
        Assert.Equal("user@example.com", result.Data.Email);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ProvisionsAFirstTimeUserWithoutTheSysAdminBootstrap()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        await BuildService().EnsureUserAndGetApprovedTenants(UserId, "user@example.com", "Test User", Authority);

        _userManagementService.Verify(
            x => x.CreateNewUser(It.Is<UserDto>(u => u.UserId == UserId), false),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsANewUserToTheAuthenticatingProvider()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        _userManagementService.Verify(
            x => x.CreateNewUser(It.Is<UserDto>(u => u.ProviderAuthority == Authority), false),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_DoesNotProvisionAnExistingUser()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        _userManagementService.Verify(
            x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ToleratesAConcurrentProvisioningConflict()
    {
        var racedUser = new User { UserId = UserId, ProviderAuthority = Authority };
        _userRepo.SetupSequence(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync((User?)null)
            .ReturnsAsync(racedUser)
            .ReturnsAsync(racedUser);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "Tenant A" } });
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("User already exists"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!.Tenants).TenantId);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_FailsWhenProvisioningFails()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.InternalServerError("database unavailable"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.False(result.IsSuccess);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsASubjectAssertedByADifferentProvider()
    {
        // The same subject from a provider the user is not registered with is a different person.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, null, null, "https://evil.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_MatchesThePinnedProviderCaseInsensitively()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = "https://Login.Example.com" });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsAnUnpinnedRecordOnFirstUse()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        AllowPinAdoption();

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.PinProviderAuthorityIfUnsetAsync(UserId, Authority), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsTheLoserOfAConcurrentFirstUsePin()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null });
        _userRepo
            .Setup(x => x.PinProviderAuthorityIfUnsetAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync("https://someone-else.example");

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsAnUnpinnedSysAdminOnFirstUse()
    {
        // Nothing pins a SysAdmin ahead of time, so refusing adoption would lock them out for good.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null, IsSysAdmin = true });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        AllowPinAdoption();

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.PinProviderAuthorityIfUnsetAsync(UserId, Authority), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsASysAdminAssertedByADifferentProvider_OncePinned()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority, IsSysAdmin = true });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            UserId, null, null, "https://evil.example");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_ReturnsOnlyTheTenantsTheUserIsApprovedFor()
    {
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "Tenant A" } });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!).TenantId);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_ReturnsAllEnabledTenants_ForASysAdmin()
    {
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(true);
        _tenantRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Tenant>
        {
            BuildTenant("000000000000000000000001", "tenant-a", "Tenant A", enabled: true),
            BuildTenant("000000000000000000000002", "tenant-disabled", "Disabled", enabled: false)
        });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!).TenantId);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_FailsClosed_WhenTheLookupThrows()
    {
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ThrowsAsync(new InvalidOperationException("mongo down"));

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }
}
