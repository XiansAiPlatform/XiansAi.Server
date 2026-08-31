using Features.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Tests.UnitTests.Features.WebApi.Services;

/// <summary>
/// A credential that names only an email address — a legacy API key, a certificate whose OU is an
/// email — cannot say which account it meant when several hold that address, so SysAdmin is never
/// resolved from a shared one. Granting it to such an account would therefore be silently
/// ineffective on exactly the paths where it matters, so the grant is refused up front.
/// </summary>
public class RoleManagementServiceSysAdminEmailTests
{
    private const string TargetUserId = "target-subject";
    private const string Email = "admin@example.com";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IUserAuthorizationInvalidator> _authorizationInvalidator = new();

    public RoleManagementServiceSysAdminEmailTests()
    {
        _tenantContext.SetupGet(x => x.UserRoles).Returns(new[] { SystemRoles.SysAdmin });
        _tenantContext.SetupGet(x => x.TenantId).Returns("tenant-a");
        _tenantContext.SetupGet(x => x.LoggedInUser).Returns("acting-admin");
    }

    private RoleManagementService BuildService() =>
        new(
            _userRepo.Object,
            _tenantContext.Object,
            NullLogger<RoleManagementService>.Instance,
            Mock.Of<IAuthProviderFactory>(),
            _authorizationInvalidator.Object);

    private void ArrangeTarget(string email, bool emailIsShared)
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(TargetUserId))
            .ReturnsAsync(new User { UserId = TargetUserId, Email = email });
        _userRepo.Setup(x => x.IsEmailSharedAsync(It.IsAny<string>(), TargetUserId))
            .ReturnsAsync(emailIsShared);
        _userRepo.Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task AssignSysAdminRolesToUserAsync_RefusesWhenAnotherAccountHoldsTheSameEmail()
    {
        ArrangeTarget(Email, emailIsShared: true);

        var result = await BuildService().AssignSysAdminRolesToUserAsync(TargetUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task AssignSysAdminRolesToUserAsync_GrantsWhenTheEmailNamesOneAccount()
    {
        ArrangeTarget(Email, emailIsShared: false);

        var result = await BuildService().AssignSysAdminRolesToUserAsync(TargetUserId);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.UpdateAsync(TargetUserId, It.Is<User>(u => u.IsSysAdmin)), Times.Once);
        _authorizationInvalidator.Verify(x => x.InvalidateAsync(TargetUserId), Times.Once);
    }

    [Fact]
    public async Task AssignSysAdminRolesToUserAsync_GrantsWhenTheAccountHasNoEmail()
    {
        // Nothing can collide with a blank address, and comparing them would match every record
        // that has none.
        ArrangeTarget(string.Empty, emailIsShared: true);

        var result = await BuildService().AssignSysAdminRolesToUserAsync(TargetUserId);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.IsEmailSharedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemoveSysAdminRolesToUserAsync_StillWorksForASharedEmail()
    {
        // Taking the role away is always the safe direction, so it is never blocked — otherwise a
        // duplicate account would be a way to keep an administrator from being demoted.
        ArrangeTarget(Email, emailIsShared: true);

        var result = await BuildService().RemoveSysAdminRolesToUserAsync(TargetUserId);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.UpdateAsync(TargetUserId, It.Is<User>(u => !u.IsSysAdmin)), Times.Once);
    }
}
