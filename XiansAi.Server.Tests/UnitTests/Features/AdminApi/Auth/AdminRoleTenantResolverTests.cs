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
    public async Task ResolveAsync_SkipsUserLookup_WhenCreatedByIsAlreadyAUserId()
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
    public async Task ResolveAsync_LooksUpByEmail_WhenCreatedByIsALegacyEmail()
    {
        _userRepo
            .Setup(x => x.GetByUserEmailAsync("admin@example.com"))
            .ReturnsAsync(new User { UserId = UserId, Email = "admin@example.com" });
        _roleCache
            .Setup(x => x.GetUserRolesAsync(UserId, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });

        var result = await BuildResolver().ResolveAsync(
            "admin@example.com", KeyOwnedBy("admin@example.com"), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        _userRepo.Verify(x => x.GetByUserEmailAsync("admin@example.com"), Times.Once);
        _roleCache.Verify(x => x.GetUserRolesAsync(UserId, TenantId), Times.Once);
    }
}
