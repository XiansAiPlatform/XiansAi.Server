using System.Net;
using System.Text.Json;
using Shared.Auth;
using Shared.Data.Models;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAgentActivationEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAgentActivationEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateGetListUpdateAndDeleteActivation_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var createResponse = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
        {
            name = "front-desk",
            agentName = agent.Name,
            description = "created by tests"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadAsJsonAsync<AgentActivation>(createResponse);
        Assert.NotNull(created);
        Assert.Equal("front-desk", created!.Name);
        Assert.Equal(agent.Name, created.AgentName);

        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var listResponse = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations?agentName={Uri.EscapeDataString(agent.Name)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains("front-desk", listBody);

        var updateResponse = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{created.Id}",
            new { description = "updated description" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadAsJsonAsync<AgentActivation>(updateResponse);
        Assert.Equal("updated description", updated!.Description);

        var deleteResponse = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task GetActivation_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{MongoDB.Bson.ObjectId.GenerateNewId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActivation_FromOtherTenant_ReturnsNotFound()
    {
        var tenantA = $"test-tenant-{Guid.NewGuid()}";
        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantA);
        await CreateTestTenantAsync(tenantB);
        await ConfigureAdminApiClientAsync(tenantA);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantA);
        var activation = await CreateTestActivationAsync(agent.Name, tenantA);

        await ConfigureAdminApiClientAsync(tenantB);
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantB}/agentActivations/{activation.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateActivation_WhenAgentMissing_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
        {
            name = "orphan",
            agentName = $"missing-agent-{Guid.NewGuid()}"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivateAgent_WhenNoWorkflowDefinitions_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/activate",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateAgent_WithMockedCleanup_ReturnsOk()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/deactivate",
            new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("deactivated", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateActivation_WithoutAdminRole_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
        {
            name = "denied",
            agentName = "any-agent"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
