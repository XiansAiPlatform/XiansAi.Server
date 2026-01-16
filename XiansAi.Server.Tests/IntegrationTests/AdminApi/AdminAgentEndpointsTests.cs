using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Services;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminAgentEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminAgentEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListAgentInstances_WithValidTenant_ReturnsAgentList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent1 = await CreateTestAgentAsync($"agent-1-{Guid.NewGuid()}", tenantId);
        var agent2 = await CreateTestAgentAsync($"agent-2-{Guid.NewGuid()}", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<AgentListResult>(response);
        Assert.NotNull(result);
        Assert.NotNull(result.Agents);
        Assert.True(result.Agents.Count >= 2);
        
        var agentNames = result.Agents.Select(a => a.Name).ToList();
        Assert.Contains(agent1.Name, agentNames);
        Assert.Contains(agent2.Name, agentNames);
    }

    [Fact]
    public async Task ListAgentInstances_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        // Create multiple agents
        for (int i = 0; i < 5; i++)
        {
            await CreateTestAgentAsync($"agent-{i}-{Guid.NewGuid()}", tenantId);
        }

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<AgentListResult>(response);
        Assert.NotNull(result);
        Assert.NotNull(result.Pagination);
        Assert.True(result.Pagination.PageSize <= 2);
    }

    [Fact]
    public async Task GetAgentInstance_WithValidId_ReturnsAgent()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Agent>(response);
        Assert.NotNull(result);
        Assert.Equal(agent.Id, result.Id);
        Assert.Equal(agent.Name, result.Name);
    }

    [Fact]
    public async Task GetAgentInstance_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAgentInstance_WithValidRequest_UpdatesAgent()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        
        var updateRequest = new UpdateAgentRequest
        {
            Name = $"updated-agent-{Guid.NewGuid()}",
            Description = "Updated description",
            OnboardingJson = "{\"key\":\"value\"}"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Agent>(response);
        Assert.NotNull(result);
        Assert.Equal(updateRequest.Name, result.Name);
        Assert.Equal(updateRequest.Description, result.Description);
    }

    [Fact]
    public async Task UpdateAgentInstance_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var invalidId = ObjectId.GenerateNewId().ToString();
        var updateRequest = new UpdateAgentRequest
        {
            Name = "updated-name"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{invalidId}", updateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAgentInstance_WithValidId_DeletesAgent()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteAgentInstance_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agents/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAgentInstances_WithoutAdminRole_ReturnsForbidden()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser); // Non-admin role
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}


