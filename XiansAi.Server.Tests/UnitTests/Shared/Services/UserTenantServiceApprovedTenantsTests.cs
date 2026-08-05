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

    private UserTenantService BuildService()
    {
        return new UserTenantService(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            _authMgtConnect.Object,
            new ConfigurationBuilder().Build(),
            _userManagementService.Object,
            _tenantRepo.Object,
            _jwtExtractor.Object);
    }

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
        Assert.Empty(result.Data!);
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
        _userRepo.SetupSequence(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync((User?)null)
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "Tenant A" } });
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("User already exists"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(UserId, null, null, Authority);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!).TenantId);
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
