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

/// <summary>
/// SysAdmin can also follow from an Azure AD group claim, re-evaluated on every WebAPI login.
/// Nobody decides that per user, so without a check a duplicate account whose address happens to
/// match a real administrator's would be promoted with no one having asked. SysAdmin is not
/// resolved from an address several accounts answer to, so the promotion is refused instead.
/// </summary>
public class UserTenantServiceGroupClaimSysAdminTests
{
    private const string UserId = "provider-subject-abc123";
    private const string AdminGroupId = "aad-admin-group";
    private const string Token = "a.jwt.token";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IJwtClaimsExtractor> _jwtExtractor = new();

    private UserTenantService BuildService() =>
        new(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            Mock.Of<IAuthMgtConnect>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Oidc:AdminGroupIds"] = AdminGroupId })
                .Build(),
            Mock.Of<IUserManagementService>(),
            Mock.Of<ITenantRepository>(),
            _jwtExtractor.Object);

    private void ArrangeLogin(string email, bool inAdminGroup, bool emailIsShared)
    {
        _tenantContext.SetupGet(x => x.LoggedInUser).Returns(UserId);
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, Email = email });
        _userRepo.Setup(x => x.IsEmailSharedAsync(It.IsAny<string>(), UserId)).ReturnsAsync(emailIsShared);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _jwtExtractor.Setup(x => x.ExtractClaims(Token, "groups"))
            .Returns(inAdminGroup ? new[] { AdminGroupId } : Array.Empty<string>());
        _jwtExtractor.Setup(x => x.ExtractClaims(Token, "roles")).Returns(Array.Empty<string>());
    }

    [Fact]
    public async Task GetCurrentUserTenants_DoesNotPromote_WhenAnotherAccountHoldsTheSameEmail()
    {
        ArrangeLogin("admin@example.com", inAdminGroup: true, emailIsShared: true);

        await BuildService().GetCurrentUserTenants(Token);

        _userRepo.Verify(x => x.SetSysAdminAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserTenants_Promotes_WhenTheEmailNamesOneAccount()
    {
        ArrangeLogin("admin@example.com", inAdminGroup: true, emailIsShared: false);

        await BuildService().GetCurrentUserTenants(Token);

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_StillDemotes_EvenWhenTheEmailIsShared()
    {
        // Failing closed is the safe direction, so a duplicate account must not become a way to
        // hold on to the role after the group membership is gone.
        ArrangeLogin("admin@example.com", inAdminGroup: false, emailIsShared: true);

        await BuildService().GetCurrentUserTenants(Token);

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, false), Times.Once);
    }
}
