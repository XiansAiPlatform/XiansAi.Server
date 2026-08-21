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

    private void ResolvesTo(string primaryUserId, params string[] roles)
    {
        _userRepo
            .Setup(x => x.ResolveEmailIdentityAsync(Email, TenantId))
            .ReturnsAsync(new EmailIdentityResolution(
                PrimaryUserId: primaryUserId,
                CandidateUserIds: new[] { primaryUserId },
                Roles: roles,
                IsSysAdmin: roles.Contains(SystemRoles.SysAdmin),
                IsAmbiguous: false));
    }

    [Fact]
    public async Task ResolveAsync_SkipsUserLookup_WhenOwnerIsAlreadyAUserId()
    {
        _roleCache
            .Setup(x => x.GetUserRolesAsync(UserId, TenantId))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });

        var result = await BuildResolver().ResolveAsync(UserId, KeyOwnedBy(UserId), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        _userRepo.Verify(x => x.ResolveEmailIdentityAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _userRepo.Verify(x => x.GetByUserIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_LooksUpRolesByCanonicalUserId_WhenOwnerIsAnEmail()
    {
        ResolvesTo(UserId, SystemRoles.TenantAdmin);

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        Assert.Equal(TenantId, result.FinalTenantId);
        _userRepo.Verify(x => x.ResolveEmailIdentityAsync(Email, TenantId), Times.Once);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotConsultTheRoleCache_ForAnEmail()
    {
        // The cache is keyed on whatever it is handed but only ever invalidated by user id, so an
        // email-keyed entry would serve roles that a revocation had already removed.
        ResolvesTo(UserId, SystemRoles.TenantAdmin);

        await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        _roleCache.Verify(x => x.GetUserRolesAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenNoUserRecordHoldsTheEmail()
    {
        _userRepo
            .Setup(x => x.ResolveEmailIdentityAsync(Email, TenantId))
            .ReturnsAsync((EmailIdentityResolution?)null);

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.False(result.Success);
        Assert.Equal(Email, result.ResolvedUserId);
    }

    [Fact]
    public async Task ResolveAsync_Fails_WhenUserHasNoAdminRole()
    {
        ResolvesTo(UserId, SystemRoles.TenantUser);

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.False(result.Success);
        Assert.Equal(UserId, result.ResolvedUserId);
        Assert.Equal("User does not have required admin role", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveAsync_DropsParticipantRoles_SoTheyMatchWhatTheCachedPathWouldReturn()
    {
        // A participant is someone an agent talks to, not someone who acts on a request.
        ResolvesTo(UserId, SystemRoles.TenantAdmin, SystemRoles.TenantParticipant);

        var result = await BuildResolver().ResolveAsync(Email, KeyOwnedBy(Email), string.Empty);

        Assert.True(result.Success);
        Assert.DoesNotContain(SystemRoles.TenantParticipant, result.UserRoles!);
    }
}
