using System.Net;
using System.Text.Json;
using Shared.Auth;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminApiKeyEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminApiKeyEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateListGetRotateAndRevoke_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = _adminUserId!;

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}",
            new { name = $"key-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();
        var rawKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrEmpty(id));
        Assert.StartsWith("sk-Xnai-", rawKey);

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(id!, await list.Content.ReadAsStringAsync());

        var get = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var rotate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}/rotate?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using var rotated = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
        var rotatedKey = rotated.RootElement.GetProperty("apiKey").GetString();
        Assert.StartsWith("sk-Xnai-", rotatedKey);
        Assert.NotEqual(rawKey, rotatedKey);

        var revoke = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}/revoke?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var getAfterRevoke = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_AsTenantAdmin_ForAnotherUser_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var other = await CreateTestUserWithRoleAsync($"other-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(other.UserId)}",
            new { name = "not-allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetApiKey_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{MongoDB.Bson.ObjectId.GenerateNewId()}?userId={Uri.EscapeDataString(_adminUserId!)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
