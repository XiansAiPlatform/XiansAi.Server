using Features.AdminApi.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;

namespace Tests.UnitTests.Features.AdminApi.Auth;

public class AdminRoleTenantResolverTests
{
    private const string TenantId = "tenant-a";
    private const string UserId = "11111111-1111-1111-1111-111111111111";
    private const string Email = "admin@example.com";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IRoleCacheService> _roleCache = new();
    private readonly Mock<ITenantCacheService> _tenantCache = new();

    private AdminRoleTenantResolver BuildResolver() =>
        new(_userRepo.Object, _roleCache.Object, _tenantCache.Object,
            NullLogger<AdminRoleTenantResolver>.Instance);

    private static ApiKey KeyOwnedBy(string createdBy) => new()
    {
        TenantId = TenantId,
        Name = "test",
        HashedKey = "hash",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = createdBy
    };

    [Fact]
    public async Task ResolveAsync_SkipsUserLookup_WhenOwnerIsAlreadyAUserId()
    {
        _roleCache
            .Setup(x => x.GetUserRolesAsync(UserId, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });

        var result = await BuildResolver().ResolveAsync(UserId, KeyOwnedBy(UserId), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        _userRepo.Verify(x => x.GetByUserEmailAsync(It.IsAny<string>()), Times.Never);
        _userRepo.Verify(x => x.GetByUserIdOrEmailAsync(It.IsAny<string>()), Times.Never);
        _userRepo.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LooksUpRolesByCanonicalUserId_WhenOwnerIsAnEmail()
    {
        _userRepo
            .Setup(x => x.GetByUserEmailAsync(Email))
            .ReturnsAsync(new User { UserId = UserId, Email = Email });
        _roleCache
            .Setup(x => x.GetUserRolesAsync(UserId, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        Assert.Equal(TenantId, result.FinalTenantId);
        _userRepo.Verify(x => x.GetByUserEmailAsync(Email), Times.Once);
        _roleCache.Verify(x => x.GetUserRolesAsync(UserId, TenantId), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_FallsBackToEmail_WhenNoUserRecordMatches()
    {
        _userRepo
            .Setup(x => x.GetByUserEmailAsync(Email))
            .ReturnsAsync((User?)null);
        _roleCache
            .Setup(x => x.GetUserRolesAsync(Email, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(Email, result.ResolvedUserId);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenUserHasNoAdminRole()
    {
        _userRepo
            .Setup(x => x.GetByUserEmailAsync(Email))
            .ReturnsAsync(new User { UserId = UserId, Email = Email });
        _roleCache
            .Setup(x => x.GetUserRolesAsync(UserId, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantUser });

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.False(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        Assert.Equal("User does not have required admin role", result.ErrorMessage);
    }
}
