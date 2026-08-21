using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class GlobalUserAdminServiceDeleteTests
{
    private const string ActingUserId = "acting-admin";
    private const string TargetUserId = "target-user";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IUserAuthorizationInvalidator> _invalidator = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();
    private readonly GlobalUserAdminService _service;

    public GlobalUserAdminServiceDeleteTests()
    {
        _webhooks.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _invalidator.Setup(x => x.InvalidateAsync(It.IsAny<User>()))
            .Returns(Task.CompletedTask);

        _service = new GlobalUserAdminService(
            _userRepo.Object,
            Mock.Of<ITenantCacheService>(),
            _invalidator.Object,
            _webhooks.Object,
            NullLogger<GlobalUserAdminService>.Instance);
    }

    [Fact]
    public async Task DeleteUserAsync_RemovesUserAndInvalidatesCaches()
    {
        var user = CreateUser(TargetUserId, isSysAdmin: false, tenant: "parkly.no");
        _userRepo.Setup(x => x.GetByUserIdAsync(TargetUserId)).ReturnsAsync(user);
        _userRepo.Setup(x => x.DeleteUser(TargetUserId, null)).ReturnsAsync(true);

        var result = await _service.DeleteUserAsync(TargetUserId, ActingUserId);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.DeleteUser(TargetUserId, null), Times.Once);
        // One call now stands for every cache the account is held in, including those of any
        // account sharing its address.
        _invalidator.Verify(x => x.InvalidateAsync(user), Times.Once);
        _webhooks.Verify(
            x => x.PublishAsync(WebhookEventTypes.UserDeleted, It.IsAny<object?>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsNotFound_WhenUserDoesNotExist()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(TargetUserId)).ReturnsAsync((User?)null);

        var result = await _service.DeleteUserAsync(TargetUserId, ActingUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        _userRepo.Verify(x => x.DeleteUser(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsForbidden_WhenDeletingSelf()
    {
        var user = CreateUser(ActingUserId, isSysAdmin: true);
        _userRepo.Setup(x => x.GetByUserIdAsync(ActingUserId)).ReturnsAsync(user);

        var result = await _service.DeleteUserAsync(ActingUserId, ActingUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Forbidden, result.StatusCode);
        _userRepo.Verify(x => x.DeleteUser(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsBadRequest_WhenDeletingLastEnabledSysAdmin()
    {
        var user = CreateUser(TargetUserId, isSysAdmin: true);
        _userRepo.Setup(x => x.GetByUserIdAsync(TargetUserId)).ReturnsAsync(user);
        _userRepo.Setup(x => x.GetSystemAdminAsync()).ReturnsAsync(new List<User>
        {
            user,
            CreateUser("locked-admin", isSysAdmin: true, lockedOut: true),
        });

        var result = await _service.DeleteUserAsync(TargetUserId, ActingUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _userRepo.Verify(x => x.DeleteUser(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_AllowsDeletingSysAdmin_WhenAnotherEnabledSysAdminRemains()
    {
        var user = CreateUser(TargetUserId, isSysAdmin: true);
        _userRepo.Setup(x => x.GetByUserIdAsync(TargetUserId)).ReturnsAsync(user);
        _userRepo.Setup(x => x.GetSystemAdminAsync()).ReturnsAsync(new List<User>
        {
            user,
            CreateUser(ActingUserId, isSysAdmin: true),
        });
        _userRepo.Setup(x => x.DeleteUser(TargetUserId, null)).ReturnsAsync(true);

        var result = await _service.DeleteUserAsync(TargetUserId, ActingUserId);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.DeleteUser(TargetUserId, null), Times.Once);
    }

    private static User CreateUser(string userId, bool isSysAdmin, string? tenant = null, bool lockedOut = false)
    {
        var user = new User
        {
            UserId = userId,
            Email = $"{userId}@example.com",
            Name = userId,
            IsSysAdmin = isSysAdmin,
            IsLockedOut = lockedOut,
        };

        if (tenant != null)
        {
            user.TenantRoles.Add(new TenantRole
            {
                Tenant = tenant,
                Roles = new List<string> { SystemRoles.TenantUser },
                IsApproved = true,
            });
        }

        return user;
    }
}
