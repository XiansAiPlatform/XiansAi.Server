using System.Net;
using Features.AdminApi.Auth;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Regression coverage for the two groups migrated onto the capability matrix.
///
/// Two claims are being proved. First, that seeding the defaults changed nothing for anyone who
/// could already call these routes — a TenantAdmin still reaches every <c>tenant.users.*</c> route
/// against an untouched matrix. Second, and the point of the whole design: a SysAdmin still reaches
/// every migrated route after every action has been seeded to "nobody", because that bypass is in
/// code and no data can revoke it.
/// </summary>
public class AdminCapabilityEnforcementTests : AdminApiIntegrationTestBase
{
    private const string MatrixRoute = "/api/v1/admin/admin-console/capability-matrix";

    public AdminCapabilityEnforcementTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    /// <summary>
    /// Denies every action the matrix can deny. The non-delegable one is skipped because a PUT
    /// naming it is rejected by design — it is already SysAdmin-only in code.
    /// </summary>
    private async Task SeedEveryActionToNobodyAsync()
    {
        foreach (var action in CapabilityActions.All.Where(a => !a.NonDelegable))
        {
            var response = await PutAsJsonAsync($"{MatrixRoute}/{action.Name}", new { allowedRoles = Array.Empty<string>() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ----- The default seeding broke nobody -----

    [Fact]
    public async Task TenantAdmin_ReachesEveryTenantUserRoute_AgainstAnUntouchedMatrix()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);
        var unknownUser = $"unknown-user-{Guid.NewGuid()}";
        var basePath = $"/api/v1/admin/tenants/{tenantId}/users";

        // A 404 from the service is proof the request got past the capability filter; what matters
        // here is that none of these is a 403.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"{basePath}?page=1&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"{basePath}/{unknownUser}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await PostAsJsonAsync(
            basePath, new { email = $"{Guid.NewGuid()}@example.com", name = "New User", role = SystemRoles.TenantParticipant })).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden,
            (await PatchAsJsonAsync($"{basePath}/{unknownUser}", new { name = "Renamed" })).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await DeleteAsync($"{basePath}/{unknownUser}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden,
            (await DeleteAsync($"{basePath}/{unknownUser}/roles/{SystemRoles.TenantParticipant}")).StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_IsStillRefusedEveryGlobalUserRoute()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);
        var target = await CreateGlobalTestUserAsync($"target-{Guid.NewGuid()}@example.com");

        // Same outcome as the deleted IsSysAdminCaller helper produced, now coming from the filter.
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/v1/admin/users?page=1&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"/api/v1/admin/users/{target.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PatchAsJsonAsync($"/api/v1/admin/users/{target.UserId}", new { name = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsJsonAsync(
            $"/api/v1/admin/users/{target.UserId}/sysadmin", new { isSysAdmin = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsJsonAsync(
            $"/api/v1/admin/users/{target.UserId}/status", new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"/api/v1/admin/users/{target.UserId}")).StatusCode);
    }

    // ----- An override takes effect, and only where it was applied -----

    [Fact]
    public async Task OverridingOneAction_DeniesOnlyThatRoute()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);

        await ConfigureAdminApiClientAsync(tenantId);
        var put = await PutAsJsonAsync(
            $"{MatrixRoute}/{CapabilityActions.TenantUsersDelete}", new { allowedRoles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        var basePath = $"/api/v1/admin/tenants/{tenantId}/users";
        var unknownUser = $"unknown-user-{Guid.NewGuid()}";

        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync($"{basePath}/{unknownUser}")).StatusCode);

        // The other five actions in the same group are untouched: the matrix is per action, not per
        // route group.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"{basePath}?page=1&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"{basePath}/{unknownUser}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, (await PostAsJsonAsync(
            basePath, new { email = $"{Guid.NewGuid()}@example.com", name = "New User", role = SystemRoles.TenantParticipant })).StatusCode);
    }

    [Fact]
    public async Task WideningAGlobalAction_LetsATenantAdminThrough()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);

        await ConfigureAdminApiClientAsync(tenantId);
        var put = await PutAsJsonAsync(
            $"{MatrixRoute}/{CapabilityActions.GlobalUsersList}",
            new { allowedRoles = new[] { SystemRoles.TenantAdmin } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        // The point of the matrix: a role that had no access gains it with a PUT, no redeploy. It is
        // also the accepted risk — widening global.users.list is cross-tenant by nature, which is why
        // the write is logged with its previous value.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/v1/admin/users?page=1&pageSize=10")).StatusCode);

        // ...and not to the rest of the group.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await DeleteAsync($"/api/v1/admin/users/unknown-user-{Guid.NewGuid()}")).StatusCode);
    }

    // ----- The invariant -----

    [Fact]
    public async Task SysAdmin_ReachesEveryMigratedRoute_AfterEveryActionIsSeededToNobody()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var target = await CreateGlobalTestUserAsync($"target-{Guid.NewGuid()}@example.com");

        await SeedEveryActionToNobodyAsync();

        var tenantUsers = $"/api/v1/admin/tenants/{tenantId}/users";
        var unknownUser = $"unknown-user-{Guid.NewGuid()}";
        var globalUsers = "/api/v1/admin/users";

        var responses = new[]
        {
            await GetAsync($"{tenantUsers}?page=1&pageSize=10"),
            await GetAsync($"{tenantUsers}/{unknownUser}"),
            await PostAsJsonAsync(tenantUsers, new { email = $"{Guid.NewGuid()}@example.com", name = "New User", role = SystemRoles.TenantParticipant }),
            await PatchAsJsonAsync($"{tenantUsers}/{unknownUser}", new { name = "Renamed" }),
            await DeleteAsync($"{tenantUsers}/{unknownUser}"),
            await DeleteAsync($"{tenantUsers}/{unknownUser}/roles/{SystemRoles.TenantParticipant}"),
            await GetAsync($"{globalUsers}?page=1&pageSize=10"),
            await GetAsync($"{globalUsers}/{target.UserId}"),
            await PatchAsJsonAsync($"{globalUsers}/{target.UserId}", new { name = "Renamed" }),
            await PutAsJsonAsync($"{globalUsers}/{target.UserId}/sysadmin", new { isSysAdmin = false }),
            await PutAsJsonAsync($"{globalUsers}/{target.UserId}/status", new { enabled = false }),
            await DeleteAsync($"{globalUsers}/{target.UserId}"),
        };

        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task SysAdmin_StillReachesTheMatrixItself_AfterEveryActionIsSeededToNobody()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        await SeedEveryActionToNobodyAsync();

        // The escape hatch has to survive too, or a bad edit would be unfixable through the API.
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(MatrixRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await DeleteAsync($"{MatrixRoute}/{CapabilityActions.TenantUsersList}")).StatusCode);
    }

    private async Task<User> CreateGlobalTestUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = ObjectId.GenerateNewId().ToString(),
            Email = email,
            Name = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>(),
        };

        await userRepository.CreateAsync(user);
        return user;
    }
}
