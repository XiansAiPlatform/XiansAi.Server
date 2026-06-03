using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;

namespace Tests.UnitTests.Shared.Services;

public class UserTenantServiceSyncSysAdminTests
{
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IAuthMgtConnect> _authMgtConnect = new();
    private readonly Mock<IUserManagementService> _userManagementService = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IJwtClaimsExtractor> _jwtExtractor = new();

    private const string UserId = "user-oid-abc123";
    private const string AdminGroupId1 = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string AdminGroupId2 = "bbbbbbbb-0000-0000-0000-000000000002";

    private UserTenantService BuildService(string? adminGroupIds)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:AdminGroupIds"] = adminGroupIds
            })
            .Build();

        _tenantContext.Setup(x => x.LoggedInUser).Returns(UserId);
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId });
        _userRepo.Setup(x => x.SetSysAdminAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        _userRepo.Setup(x => x.IsSysAdmin(UserId))
            .ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto>());

        return new UserTenantService(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            _authMgtConnect.Object,
            config,
            _userManagementService.Object,
            _tenantRepo.Object,
            _jwtExtractor.Object);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SkipsSysAdminSync_WhenAdminGroupIdsNotConfigured()
    {
        var service = BuildService(adminGroupIds: null);

        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SkipsSysAdminSync_WhenAdminGroupIdsIsEmpty()
    {
        var service = BuildService(adminGroupIds: string.Empty);

        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminTrue_WhenGroupsClaimMatchesSingleConfiguredId()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([AdminGroupId1, "unrelated-group-id"]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: AdminGroupId1);
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminFalse_WhenNoGroupsMatchConfiguredId()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns(["unrelated-group-id"]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: AdminGroupId1);
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, false), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminTrue_WhenAnyOfMultipleConfiguredGroupIdsMatch()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([AdminGroupId2]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: $"{AdminGroupId1},{AdminGroupId2}");
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminTrue_WhenRolesClaimMatchesConfiguredId()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([AdminGroupId1]);

        var service = BuildService(adminGroupIds: AdminGroupId1);
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminTrue_WhenConfigHasWhitespaceAroundGroupIds()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([AdminGroupId1]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: $"  {AdminGroupId1}  ,  {AdminGroupId2}  ");
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_SetsSysAdminTrue_WhenGroupIdMatchIsCaseInsensitive()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([AdminGroupId1.ToUpper()]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: AdminGroupId1.ToLower());
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, true), Times.Once);
    }

    [Fact]
    public async Task GetCurrentUserTenants_CallsSetSysAdminOnce_PerLogin()
    {
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "groups"))
            .Returns([AdminGroupId1]);
        _jwtExtractor.Setup(x => x.ExtractClaims(It.IsAny<string>(), "roles"))
            .Returns([]);

        var service = BuildService(adminGroupIds: AdminGroupId1);
        await service.GetCurrentUserTenants(BuildToken());

        _userRepo.Verify(x => x.SetSysAdminAsync(UserId, It.IsAny<bool>()), Times.Once);
    }

    private static string BuildToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-32-chars-minimum!"));
        var token = new JwtSecurityToken(
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
