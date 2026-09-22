using System.Net;
using System.Text.Json;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAppIntegrationEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAppIntegrationEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetIntegrationTypes_ReturnsSupportedPlatforms()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync("/api/v1/admin/integrations/metadata/types");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("slack", body);
        Assert.Contains("webhook", body);
    }

    [Fact]
    public async Task CreateEnableDisableUpdateAndDeleteWebhookIntegration_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "hooks");

        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/integrations", new
        {
            platformId = "webhook",
            name = $"hook-{Guid.NewGuid()}",
            agentName = agent.Name,
            activationName = activation.Name,
            isEnabled = false
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(id));

        var get = await GetAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/integrations?agentName={Uri.EscapeDataString(agent.Name)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(id!, await list.Content.ReadAsStringAsync());

        var enable = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}/enable", new { });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);

        var disable = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}/disable", new { });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var update = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}", new
        {
            description = "updated webhook"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var webhookUrl = await GetAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}/webhook-url");
        Assert.Equal(HttpStatusCode.OK, webhookUrl.StatusCode);

        var delete = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var getAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/integrations/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task CreateIntegration_WithUnknownPlatform_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/integrations", new
        {
            platformId = "not-a-platform",
            name = "bad",
            agentName = agent.Name,
            activationName = "none"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetIntegration_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/integrations/{MongoDB.Bson.ObjectId.GenerateNewId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
