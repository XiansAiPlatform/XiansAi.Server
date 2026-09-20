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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge?agentName={agent.Name}");

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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}");

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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{invalidId}");

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
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/knowledge?agentName={agent.Name}", request);

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
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}", request);

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
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}");
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
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/test-knowledge/versions?agentName={agent.Name}");

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

        // Act - delete all versions at the tenant scope level
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/test-knowledge/tenant/versions?agentName={agent.Name}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestKnowledge_ReturnsTenantScopedItem()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        await CreateTestKnowledgeAsync("playbook", agent.Name, "tenant copy", tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/latest?name=playbook&agentName={Uri.EscapeDataString(agent.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var latest = await ReadAsJsonAsync<Knowledge>(response);
        Assert.Equal("playbook", latest!.Name);
        Assert.Equal("tenant copy", latest.Content);
    }

    [Fact]
    public async Task GetLatestKnowledge_WithMissingName_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/latest?agentName={Uri.EscapeDataString(agent.Name)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverrideKnowledge_AtActivation_CreatesMoreSpecificCopy()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync("playbook", agent.Name, "tenant copy", tenantId);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}/override/activation?activationName=front-desk",
            new { });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsJsonAsync<Knowledge>(response);
        Assert.Equal("front-desk", created!.ActivationName);
        Assert.Equal("playbook", created.Name);
        Assert.NotEqual(knowledge.Id, created.Id);

        var latest = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/latest?name=playbook&agentName={Uri.EscapeDataString(agent.Name)}&activationName=front-desk");
        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var resolved = await ReadAsJsonAsync<Knowledge>(latest);
        Assert.Equal(created.Id, resolved!.Id);
    }

    [Fact]
    public async Task OverrideKnowledge_WithInvalidLevel_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync("playbook", agent.Name, "tenant copy", tenantId);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}/override/system",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteKnowledgeByActivation_RemovesActivationScopedItems()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var knowledge = await CreateTestKnowledgeAsync(
            "playbook", agent.Name, "activation copy", tenantId, activationName: "front-desk");

        var response = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge/agents/{Uri.EscapeDataString(agent.Name)}/activation/front-desk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var getAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/knowledge/{knowledge.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }
}
