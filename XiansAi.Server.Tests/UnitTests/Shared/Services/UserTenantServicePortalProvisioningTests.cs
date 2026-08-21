using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers the portal path that creates a user on first sign-in via GetCurrentUserTenants.
///
/// That path previously treated every Conflict as success, including the email-collision kind
/// where no record exists under the presented user id — leaving the caller acting as an identity
/// that was never written.
/// </summary>
public class UserTenantServicePortalProvisioningTests
{
    private const string UserId = "portal-subject-guid";
    private const string Token = "unused-token-body";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IUserManagementService> _userManagementService = new();
    private readonly Mock<IJwtClaimsExtractor> _jwtExtractor = new();

    private UserTenantService BuildService()
    {
        _tenantContext.Setup(x => x.LoggedInUser).Returns(UserId);

        return new UserTenantService(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            new Mock<IAuthMgtConnect>().Object,
            new ConfigurationBuilder().Build(),
            _userManagementService.Object,
            new Mock<ITenantRepository>().Object,
            _jwtExtractor.Object);
    }

    private void ArrangeFirstSignIn(string email)
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _jwtExtractor.Setup(x => x.ValidateAndExtractClaimsAsync(Token))
            .ReturnsAsync(JwtValidationResult.Success(UserId, email, "Portal User"));
    }

    [Fact]
    public async Task GetCurrentUserTenants_RefusesWhenEmailBelongsToAnotherAccount()
    {
        // Conflict on email, and re-read finds no record under this user id — the previous bug
        // treated this as success and returned a UserDto with nothing behind it.
        ArrangeFirstSignIn("taken@example.com");
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("A user with this email already exists"));

        var result = await BuildService().GetCurrentUserTenants(Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        Assert.Equal("This email is already registered to a different account", result.ErrorMessage);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SucceedsWhenUserAlreadyExistsById()
    {
        // Genuine creation race: the conflict is on user id, and the record is there.
        ArrangeFirstSignIn("new@example.com");
        // The record appears once the concurrent request has created it, rather than after a fixed
        // number of reads, so the test does not depend on how often it is looked up.
        var racedUser = new User { UserId = UserId, Email = "new@example.com" };
        var exists = false;
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(() => exists ? racedUser : null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .Callback(() => exists = true)
            .ReturnsAsync(ServiceResult<bool>.Conflict("User already exists"));

        var result = await BuildService().GetCurrentUserTenants(Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SucceedsWhenUserIsCreated()
    {
        ArrangeFirstSignIn("new@example.com");
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        var result = await BuildService().GetCurrentUserTenants(Token);

        Assert.True(result.IsSuccess);
        _userManagementService.Verify(
            x => x.CreateNewUser(It.Is<UserDto>(u => u.UserId == UserId && u.Email == "new@example.com"), true),
            Times.Once);
    }
}
