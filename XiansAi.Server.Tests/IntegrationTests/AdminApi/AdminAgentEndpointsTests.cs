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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments");

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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments?page=1&pageSize=2");

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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}");

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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{invalidId}");

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
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}", updateRequest);

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
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{invalidId}", updateRequest);

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
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}");
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
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListAgentInstances_WithoutAdminRole_ReturnsUnauthorized()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser); // Non-admin role
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAgentDeployment_WithActivations_ReturnsConflict()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        
        // Create an activation for the agent
        var activation = await CreateTestActivationAsync(agent.Name, tenantId);

        // Act - Try to delete the agent deployment
        var deleteResponse = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}");

        // Assert - Should fail with Conflict
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        
        var errorContent = await deleteResponse.Content.ReadAsStringAsync();
        Assert.Contains("activation", errorContent, StringComparison.OrdinalIgnoreCase);
        
        // Cleanup - Delete the activation first, then the agent should be deletable
        await DeleteTestActivationAsync(activation.Id);
        
        var deleteAfterCleanup = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agentDeployments/{agent.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteAfterCleanup.StatusCode);
    }
}


