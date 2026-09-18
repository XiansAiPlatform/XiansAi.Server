using System.Net;
using System.Text.Json;
using Features.AdminApi.Auth;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// The SysAdmin-only CRUD surface for the capability matrix, and the round trip that matters most:
/// an override takes effect and deleting it reverts the action to its code default rather than
/// closing it off.
/// </summary>
public class AdminCapabilityMatrixEndpointsTests : AdminApiIntegrationTestBase
{
    private const string MatrixRoute = "/api/v1/admin/admin-console/capability-matrix";

    public AdminCapabilityMatrixEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static JsonElement ActionEntry(JsonElement matrix, string action) =>
        matrix.EnumerateArray().Single(e => e.GetProperty("action").GetString() == action);

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task GetMatrix_AsSysAdmin_ListsEveryDeclaredActionWithItsDefault()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(MatrixRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var matrix = await ReadJsonAsync(response);
        Assert.Equal(CapabilityActions.All.Count, matrix.GetArrayLength());

        // Seeded rather than absent: every action is visible from day one, so an empty rule reads as
        // intentional rather than as "nobody has configured this yet".
        var delete = ActionEntry(matrix, CapabilityActions.TenantUsersDelete);
        Assert.Equal("default", delete.GetProperty("source").GetString());
        Assert.Equal(new[] { SystemRoles.TenantAdmin }, Strings(delete.GetProperty("allowedRoles")));

        var globalList = ActionEntry(matrix, CapabilityActions.GlobalUsersList);
        Assert.Empty(Strings(globalList.GetProperty("allowedRoles")));
    }

    [Fact]
    public async Task GetMatrix_ReportsSysAdminInEffectiveRolesButNotAllowedRoles()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var matrix = await ReadJsonAsync(await GetAsync(MatrixRoute));
        var entry = ActionEntry(matrix, CapabilityActions.TenantUsersDelete);

        // SysAdmin is never a stored role, so the view has to say so — otherwise a reader concludes
        // SysAdmin is excluded and "fixes" it by adding a role string that does nothing.
        Assert.DoesNotContain(SystemRoles.SysAdmin, Strings(entry.GetProperty("allowedRoles")));
        Assert.Contains(SystemRoles.SysAdmin, Strings(entry.GetProperty("effectiveRoles")));
    }

    [Fact]
    public async Task GetCatalog_ReturnsTheCompiledActionList()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var catalog = await ReadJsonAsync(await GetAsync($"{MatrixRoute}/catalog"));
        var actions = catalog.GetProperty("actions");

        Assert.Equal(CapabilityActions.All.Count, actions.GetArrayLength());

        var sysAdminSet = actions.EnumerateArray()
            .Single(e => e.GetProperty("action").GetString() == CapabilityActions.GlobalUsersSysAdminSet);
        Assert.True(sysAdminSet.GetProperty("nonDelegable").GetBoolean());

        // The catalog publishes each action's code default, so an operator can see what a DELETE
        // would revert to before issuing one.
        Assert.Equal(
            new[] { SystemRoles.TenantAdmin },
            Strings(actions.EnumerateArray()
                .Single(e => e.GetProperty("action").GetString() == CapabilityActions.TenantUsersDelete)
                .GetProperty("defaultRoles")));
    }

    [Fact]
    public async Task PutThenDelete_OverridesTheDefaultAndThenRevertsToIt()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var put = await PutAsJsonAsync(
            $"{MatrixRoute}/{CapabilityActions.TenantUsersList}",
            new { allowedRoles = new[] { SystemRoles.TenantUser }, description = "narrowed for a test" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var overridden = ActionEntry(await ReadJsonAsync(await GetAsync(MatrixRoute)), CapabilityActions.TenantUsersList);
        Assert.Equal("override", overridden.GetProperty("source").GetString());
        Assert.Equal(new[] { SystemRoles.TenantUser }, Strings(overridden.GetProperty("allowedRoles")));
        Assert.Equal("narrowed for a test", overridden.GetProperty("description").GetString());
        Assert.Equal(_adminUserId, overridden.GetProperty("updatedBy").GetString());

        var delete = await DeleteAsync($"{MatrixRoute}/{CapabilityActions.TenantUsersList}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        // Deleting a rule reverts to the code default, never to deny — the opposite of what deleting
        // the OIDC config does, and deliberately so.
        var reverted = ActionEntry(await ReadJsonAsync(await GetAsync(MatrixRoute)), CapabilityActions.TenantUsersList);
        Assert.Equal("default", reverted.GetProperty("source").GetString());
        Assert.Equal(new[] { SystemRoles.TenantAdmin }, Strings(reverted.GetProperty("allowedRoles")));
    }

    [Fact]
    public async Task Put_ForANonDelegableAction_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Granting this to another role would be a one-request path out of the tenant boundary.
        var response = await PutAsJsonAsync(
            $"{MatrixRoute}/{CapabilityActions.GlobalUsersSysAdminSet}",
            new { allowedRoles = new[] { SystemRoles.TenantAdmin } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_ForAnUnknownAction_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PutAsJsonAsync(
            $"{MatrixRoute}/some.action.nobody.declared",
            new { allowedRoles = new[] { SystemRoles.TenantAdmin } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithNoStoredRule_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await DeleteAsync($"{MatrixRoute}/{CapabilityActions.TenantUsersGet}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EveryRoute_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(MatrixRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync($"{MatrixRoute}/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsJsonAsync(
            $"{MatrixRoute}/{CapabilityActions.TenantUsersList}",
            new { allowedRoles = new[] { SystemRoles.TenantAdmin } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await DeleteAsync($"{MatrixRoute}/{CapabilityActions.TenantUsersList}")).StatusCode);
    }
}
