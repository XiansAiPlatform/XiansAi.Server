using System.Net;
using Shared.Auth;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Deterministic HTTP coverage for Admin API routes that depend on Temporal at runtime.
/// These tests assert validation and authz without talking to a live Temporal cluster.
/// </summary>
public class AdminTemporalAdjacentEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminTemporalAdjacentEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetStats_WithoutDateRange_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/stats");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListTasks_WithActivationButNoAgent_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/tasks?activationName=front-desk");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WithMissingParameters_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var missingAgent = await GetAsync($"/api/v1/admin/tenants/{tenantId}/heartbeat?activationName=front-desk");
        Assert.Equal(HttpStatusCode.BadRequest, missingAgent.StatusCode);

        var missingActivation = await GetAsync($"/api/v1/admin/tenants/{tenantId}/heartbeat?agentName=any-agent");
        Assert.Equal(HttpStatusCode.BadRequest, missingActivation.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WhenConversationalFlowMissing_ReturnsUnavailable()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/heartbeat?agentName={Uri.EscapeDataString(agent.Name)}&activationName=front-desk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"available\":false", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetScheduleById_WithoutScheduleId_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{Uri.EscapeDataString(agent.Name)}/schedules/by-id");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkerDeployments_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/worker-deployments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
