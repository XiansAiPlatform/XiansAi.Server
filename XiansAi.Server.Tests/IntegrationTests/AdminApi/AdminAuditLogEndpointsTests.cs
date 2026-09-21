using System.Net;
using System.Text.Json;
using Shared.Auth;
using Shared.Auditing;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAuditLogEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAuditLogEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListTenantAuditLogs_ReturnsSeededEntry()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var performedBy = _adminUserId!;
        await SeedAuditLogAsync(tenantId, "agent.access.changed", performedBy, "front-desk");

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains("agent.access.changed", body);

        var performedByOptions = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs/performed-by");
        Assert.Equal(HttpStatusCode.OK, performedByOptions.StatusCode);
        Assert.Contains(performedBy, await performedByOptions.Content.ReadAsStringAsync());

        var activationNames = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs/activation-names");
        Assert.Equal(HttpStatusCode.OK, activationNames.StatusCode);
        Assert.Contains("front-desk", await activationNames.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListPlatformAuditLogs_AsSysAdmin_ReturnsSeededEntry()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        await SeedAuditLogAsync(AuditLogTenants.Platform, "user.sysadmin.granted", _adminUserId!);

        var response = await GetAsync("/api/v1/admin/platform/audit-logs?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("user.sysadmin.granted", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListPlatformAuditLogs_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync("/api/v1/admin/platform/audit-logs?page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListTenantAuditLogs_ForPlatformTenantId_ReturnsNotFound()
    {
        var tenantId = AuditLogTenants.Platform;
        await ConfigureAdminApiClientAsync("test-tenant");

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/audit-logs");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 404 or 403, got {response.StatusCode}");
    }
}
