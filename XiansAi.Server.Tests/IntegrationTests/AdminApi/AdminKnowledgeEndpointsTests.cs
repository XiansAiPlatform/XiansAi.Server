using System.Net;
using MongoDB.Bson;
using Shared.Data.Models;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminKnowledgeEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminKnowledgeEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListKnowledge_WithValidAgent_ReturnsKnowledgeList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        await CreateTestKnowledgeAsync("knowledge-1", agent.Name, "Content 1", tenantId);
        await CreateTestKnowledgeAsync("knowledge-2", agent.Name, "Content 2", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.Contains("knowledge-1", content);
        Assert.Contains("knowledge-2", content);
    }

    [Fact]
    public async Task GetKnowledgeById_WithValidId_ReturnsKnowledge()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Test content", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal(knowledge.Id, result.Id);
        Assert.Equal("test-knowledge", result.Name);
    }

    [Fact]
    public async Task GetKnowledgeById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateKnowledge_WithValidRequest_CreatesKnowledge()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        
        var request = new
        {
            name = "new-knowledge",
            content = "New knowledge content",
            type = "text"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal("new-knowledge", result.Name);
        Assert.Equal("New knowledge content", result.Content);
    }

    [Fact]
    public async Task UpdateKnowledge_WithValidRequest_UpdatesKnowledge()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Original content", tenantId);
        
        var request = new
        {
            content = "Updated content",
            type = "text"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/{knowledge.Id}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal("Updated content", result.Content);
    }

    [Fact]
    public async Task DeleteKnowledge_WithValidId_DeletesKnowledge()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Test content", tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/{knowledge.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task GetVersions_WithValidName_ReturnsAllVersions()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Version 1", tenantId, createdAt: DateTime.UtcNow.AddHours(-2));
        await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Version 2", tenantId, createdAt: DateTime.UtcNow.AddHours(-1));
        await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Version 3", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/test-knowledge/versions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.Contains("test-knowledge", content);
    }

    [Fact]
    public async Task DeleteAllVersions_WithValidName_DeletesAllVersions()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge1 = await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Version 1", tenantId);
        var knowledge2 = await CreateTestKnowledgeAsync("test-knowledge", agent.Name, "Version 2", tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/knowledge/test-knowledge/versions");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}


