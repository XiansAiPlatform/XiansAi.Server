using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// Pins the one property the AdminApi capability matrix's SysAdmin bypass rests on: role resolution
/// reports SysAdmin from the account flag, independently of tenant membership.
///
/// Why this needs its own test. <c>CapabilityMatrixFilter</c> decides the bypass by asking whether
/// <c>ITenantContext.UserRoles</c> contains SysAdmin. On the keyless (X-User-Token) path, a SysAdmin
/// calling a route marked <c>TenantOptionalForSysAdminMetadata</c> is resolved against the
/// placeholder tenant "none" (<c>AdminKeylessUserResolver</c>), a tenant no account is a member of —
/// so the roles come back from a membership lookup that matches nothing. The bypass survives that
/// only because the SysAdmin flag is appended afterwards regardless. Move that append under the
/// membership lookup and every SysAdmin bypass on a tenant-optional route fails at once, with no
/// other test noticing.
///
/// Runs against a real MongoDB (ephemeral fixture) so the query is exercised rather than mocked.
/// </summary>
public class UserRepositorySysAdminRoleTests : IClassFixture<MongoDbFixture>
{
    private const string PlaceholderTenant = "none";

    private readonly UserRepository _repository;
    private readonly IMongoCollection<User> _users;

    public UserRepositorySysAdminRoleTests(MongoDbFixture fixture)
    {
        _users = fixture.Database.GetCollection<User>("users");

        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(fixture.Database);

        _repository = new UserRepository(
            databaseService.Object,
            NullLogger<UserRepository>.Instance,
            new Mock<ITenantRepository>().Object);
    }

    private async Task<string> InsertUserAsync(bool isSysAdmin, params TenantRole[] tenantRoles)
    {
        var userId = $"user-{Guid.NewGuid()}";
        await _users.InsertOneAsync(new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Email = $"{userId}@example.com",
            IsSysAdmin = isSysAdmin,
            TenantRoles = tenantRoles.ToList(),
        });
        return userId;
    }

    [Fact]
    public async Task GetUserRoles_ReportsSysAdmin_ForATenantTheAccountIsNotAMemberOf()
    {
        var userId = await InsertUserAsync(isSysAdmin: true);

        var roles = await _repository.GetUserRolesAsync(userId, PlaceholderTenant);

        Assert.Contains(SystemRoles.SysAdmin, roles);
    }

    [Fact]
    public async Task GetUserRoles_ReportsSysAdmin_AlongsideARealTenantMembership()
    {
        var userId = await InsertUserAsync(
            isSysAdmin: true,
            new TenantRole { Tenant = "tenant-a", Roles = new List<string> { SystemRoles.TenantAdmin }, IsApproved = true });

        var roles = await _repository.GetUserRolesAsync(userId, "tenant-a");

        Assert.Contains(SystemRoles.SysAdmin, roles);
        Assert.Contains(SystemRoles.TenantAdmin, roles);
    }

    [Fact]
    public async Task GetUserRoles_DoesNotInventSysAdmin_ForAnAccountWithoutTheFlag()
    {
        var userId = await InsertUserAsync(
            isSysAdmin: false,
            new TenantRole { Tenant = "tenant-a", Roles = new List<string> { SystemRoles.TenantAdmin }, IsApproved = true });

        Assert.DoesNotContain(SystemRoles.SysAdmin, await _repository.GetUserRolesAsync(userId, "tenant-a"));
        Assert.DoesNotContain(SystemRoles.SysAdmin, await _repository.GetUserRolesAsync(userId, PlaceholderTenant));
    }

    [Fact]
    public async Task GetUserRoles_DropsSysAdmin_ForADisabledAccount()
    {
        var userId = $"user-{Guid.NewGuid()}";
        await _users.InsertOneAsync(new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Email = $"{userId}@example.com",
            IsSysAdmin = true,
            IsLockedOut = true,
        });

        // Disabling an account is the one thing that does take the bypass away.
        Assert.Empty(await _repository.GetUserRolesAsync(userId, PlaceholderTenant));
    }
}
