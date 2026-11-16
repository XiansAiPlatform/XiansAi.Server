using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
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
    public async Task CountActivationsByAgent_ReturnsPerAgentCounts()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        var otherTenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        await CreateTestTenantAsync(otherTenantId);

        var agentA = $"agent-a-{Guid.NewGuid()}";
        var agentB = $"agent-b-{Guid.NewGuid()}";
        var agentNoActivations = $"agent-c-{Guid.NewGuid()}";

        await CreateTestActivationAsync(agentA, tenantId, isActive: true);
        await CreateTestActivationAsync(agentA, tenantId);
        await CreateTestActivationAsync(agentA, tenantId);
        await CreateTestActivationAsync(agentB, tenantId, isActive: true);
        await CreateTestActivationAsync(agentA, otherTenantId); // other tenant: must not be counted

        // Repeated params, comma-separated values and duplicates are all accepted.
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/counts" +
            $"?agentNames={Uri.EscapeDataString(agentA)}" +
            $"&agentNames={Uri.EscapeDataString($"{agentB},{agentNoActivations},{agentA}")}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = doc.RootElement.EnumerateArray()
            .Select(e => (
                Name: e.GetProperty("agentName").GetString(),
                Total: e.GetProperty("instanceCount").GetInt64(),
                Active: e.GetProperty("activeInstanceCount").GetInt64()))
            .ToList();

        Assert.Equal(
            [(agentA, 3L, 1L), (agentB, 1L, 1L), (agentNoActivations, 0L, 0L)],
            items);

        // Without agentNames every agent with activations in the tenant is returned.
        var allResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/counts");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        using var allDoc = JsonDocument.Parse(await allResponse.Content.ReadAsStringAsync());
        var allNames = allDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("agentName").GetString())
            .ToList();
        Assert.Equal([agentA, agentB], allNames);
    }

    [Fact]
    public async Task CountActivationsByAgent_ActiveCount_FollowsActiveAndLegacyRules()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = $"agent-{Guid.NewGuid()}";
        var now = DateTime.UtcNow;

        // Counted as active
        await InsertActivationAsync(agent, tenantId, active: true, activatedAt: now, deactivatedAt: null);
        await InsertActivationAsync(agent, tenantId, active: true, activatedAt: null, deactivatedAt: now); // explicit flag wins
        await InsertActivationAsync(agent, tenantId, active: null, activatedAt: now, deactivatedAt: null); // legacy, running
        await InsertLegacyActivationWithoutActiveFieldAsync(agent, tenantId, activatedAt: now);         // legacy, field missing

        // Counted as inactive
        await InsertActivationAsync(agent, tenantId, active: false, activatedAt: now, deactivatedAt: null); // explicit flag wins
        await InsertActivationAsync(agent, tenantId, active: null, activatedAt: now, deactivatedAt: now);   // legacy, stopped
        await InsertActivationAsync(agent, tenantId, active: null, activatedAt: null, deactivatedAt: null); // never activated

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/counts?agentNames={Uri.EscapeDataString(agent)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal(7, item.GetProperty("instanceCount").GetInt64());
        Assert.Equal(4, item.GetProperty("activeInstanceCount").GetInt64());
    }

    [Fact]
    public async Task CountActivationsByAgent_ForTenantWithoutActivations_ReturnsEmptyList()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/counts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task CountActivationsByAgent_AsTenantAdminOfSameTenant_ReturnsOk()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        var agent = $"agent-{Guid.NewGuid()}";
        await CreateTestActivationAsync(agent, tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/counts?agentNames={Uri.EscapeDataString(agent)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, Assert.Single(doc.RootElement.EnumerateArray()).GetProperty("instanceCount").GetInt64());
    }

    [Fact]
    public async Task CountActivationsByAgent_AsTenantAdminOfOtherTenant_IsRejected()
    {
        var ownTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(ownTenant);
        await CreateTestTenantAsync(otherTenant);
        await ConfigureAdminApiClientAsync(ownTenant, SystemRoles.TenantAdmin);
        await CreateTestActivationAsync($"agent-{Guid.NewGuid()}", otherTenant);

        var response = await GetAsync($"/api/v1/admin/tenants/{otherTenant}/agentActivations/counts");

        // A tenant-bound admin key is refused at authentication for another tenant's route,
        // matching the other AdminApi cross-tenant checks.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CountActivationsByAgent_WithoutAdminRole_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations/counts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task InsertActivationAsync(
        string agentName, string tenantId, bool? active, DateTime? activatedAt, DateTime? deactivatedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IActivationRepository>();
        await repository.CreateAsync(new AgentActivation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = $"activation-{Guid.NewGuid()}",
            AgentName = agentName,
            TenantId = tenantId,
            CreatedBy = "test-user",
            Active = active,
            ActivatedAt = activatedAt,
            DeactivatedAt = deactivatedAt
        });
    }

    /// <summary>Inserts a pre-"active"-field document, as written by older server versions.</summary>
    private async Task InsertLegacyActivationWithoutActiveFieldAsync(string agentName, string tenantId, DateTime activatedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var database = await scope.ServiceProvider.GetRequiredService<IDatabaseService>().GetDatabaseAsync();
        await database.GetCollection<BsonDocument>("activations").InsertOneAsync(new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["name"] = $"legacy-{Guid.NewGuid()}",
            ["agent_name"] = agentName,
            ["tenant_id"] = tenantId,
            ["created_by"] = "test-user",
            ["created_at"] = DateTime.UtcNow,
            ["workflow_ids"] = new BsonArray(),
            ["activated_at"] = activatedAt
        });
    }

    [Fact]
    public async Task CountActivationsByAgent_WithTooManyNames_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var names = string.Join(",", Enumerable.Range(0, 101).Select(i => $"agent-{i}"));
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/counts?agentNames={Uri.EscapeDataString(names)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
