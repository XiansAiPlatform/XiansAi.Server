using System.Net;
using System.Text.Json;
using Shared.Auth;
using Shared.Data.Models;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAgentAccessEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAgentAccessEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GrantChangeAndRevokeAccess_UpdatesAgentLists()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var member = await CreateTestUserWithRoleAsync($"writer-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var grant = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users", new
        {
            userId = member.UserId,
            level = "Write"
        });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Contains(member.UserId, await grant.Content.ReadAsStringAsync());

        var get = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
        {
            Assert.Contains(member.UserId, ReadStringArray(doc.RootElement.GetProperty("writeAccess")));
        }

        var patch = await PatchAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users/{member.UserId}",
            new { level = "Read" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using (var doc = JsonDocument.Parse(await patch.Content.ReadAsStringAsync()))
        {
            Assert.Contains(member.UserId, ReadStringArray(doc.RootElement.GetProperty("readAccess")));
            Assert.DoesNotContain(member.UserId, ReadStringArray(doc.RootElement.GetProperty("writeAccess")));
        }

        var revoke = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users/{member.UserId}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        using (var doc = JsonDocument.Parse(await revoke.Content.ReadAsStringAsync()))
        {
            Assert.DoesNotContain(member.UserId, ReadStringArray(doc.RootElement.GetProperty("readAccess")));
        }
    }

    [Fact]
    public async Task GetTenantAgentAccess_ReturnsGrantedLevel()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var member = await CreateTestUserWithRoleAsync($"reader-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var grant = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users", new
        {
            userId = member.UserId,
            level = "Owner"
        });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agent-access?user={Uri.EscapeDataString(member.UserId)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Owner", doc.RootElement.GetProperty("agents").GetProperty(agent.Name).GetString());
    }

    [Fact]
    public async Task GetTenantAgentAccess_WithoutUser_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agent-access");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GrantAccess_WithEmailInsteadOfUserId_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var member = await CreateTestUserWithRoleAsync($"member-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users", new
        {
            userId = member.Email,
            level = "Read"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GrantAccess_WithInvalidLevel_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var member = await CreateTestUserWithRoleAsync($"member-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/access/users", new
        {
            userId = member.UserId,
            level = "Superuser"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAccess_WithUnknownAgent_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{MongoDB.Bson.ObjectId.GenerateNewId()}/access");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static IEnumerable<string> ReadStringArray(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString() ?? string.Empty);
}
