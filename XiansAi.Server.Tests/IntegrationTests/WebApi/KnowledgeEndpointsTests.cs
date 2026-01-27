using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Xunit;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Data;
using Tests.TestUtils;

namespace Tests.IntegrationTests.WebApi;

public class KnowledgeEndpointsTests : WebApiIntegrationTestBase
{
    private const string TestUserId = "test-user";

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.WebApi.KnowledgeEndpointsTests.GetLatestAll_WithValidTenant_ReturnsKnowledgeList"
    */

    public KnowledgeEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetLatestAll_WithValidTenant_ReturnsKnowledgeList()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        await CreateTestKnowledgeAsync("knowledge-1", agentName, "Content 1");
        await CreateTestKnowledgeAsync("knowledge-2", agentName, "Content 2");

        // Act
        var response = await GetAsync($"/api/client/knowledge/latest/all?agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var groupedResponse = await ReadAsJsonAsync<GroupedKnowledgeResponse>(response);
        Assert.NotNull(groupedResponse);
        Assert.True(groupedResponse.Groups.Count >= 2);
        
        var groupNames = groupedResponse.Groups.Select(g => g.Name).ToList();
        Assert.Contains("knowledge-1", groupNames);
        Assert.Contains("knowledge-2", groupNames);
    }

    [Fact]
    public async Task GetById_WithValidId_ReturnsKnowledge()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Test content");

        // Act
        var response = await GetAsync($"/api/client/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal(knowledge.Id, result.Id);
        Assert.Equal("test-knowledge", result.Name);
        Assert.Equal("Test content", result.Content);
        Assert.Equal(agentName, result.Agent);
    }

    [Fact]
    public async Task GetById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/client/knowledge/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithValidRequest_CreatesKnowledge()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var request = new KnowledgeRequest
        {
            Name = "new-knowledge",
            Content = "New knowledge content",
            Type = "text",
            Agent = agentName
        };

        // Act
        var response = await PostAsJsonAsync("/api/client/knowledge/", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal("new-knowledge", result.Name);
        Assert.Equal("New knowledge content", result.Content);
        Assert.Equal("text", result.Type);
        Assert.Equal(agentName, result.Agent);
        Assert.Equal(TestTenantId, result.TenantId);
        Assert.NotNull(result.Version);
        Assert.NotEmpty(result.Version);
    }

    [Fact]
    public async Task Create_WithUnauthorizedAgent_ReturnsForbidden()
    {
        // Arrange
        var unauthorizedAgent = $"unauthorized-agent-{Guid.NewGuid()}";
        var request = new KnowledgeRequest
        {
            Name = "test-knowledge",
            Content = "Test content",
            Type = "text",
            Agent = unauthorizedAgent
        };

        // Act
        var response = await PostAsJsonAsync("/api/client/knowledge/", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.WebApi.KnowledgeEndpointsTests.GetLatestByName_WithValidNameAndAgent_ReturnsKnowledge"
    */
    [Fact]
    public async Task GetLatestByName_WithValidNameAndAgent_ReturnsKnowledge()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var knowledge1 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Old content", createdAt: DateTime.UtcNow.AddHours(-1));
        var knowledge2 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Latest content");

        // Act
        var response = await GetAsync($"/api/client/knowledge/latest?name=test-knowledge&agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge>(response);
        Assert.NotNull(result);
        Assert.Equal("test-knowledge", result.Name);
        //Assert.Equal("Latest content", result.Content);
        //Assert.Equal(knowledge2.Id, result.Id);
    }

    [Fact]
    public async Task GetLatestByName_WithNonExistentName_ReturnsNotFound()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);

        // Act
        var response = await GetAsync($"/api/client/knowledge/latest?name=non-existent&agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteById_WithValidId_DeletesKnowledge()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Test content");

        // Act
        var response = await DeleteAsync($"/api/client/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify deletion
        var getResponse = await GetAsync($"/api/client/knowledge/{knowledge.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await DeleteAsync($"/api/client/knowledge/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteById_WithUnauthorizedAgent_ReturnsForbidden()
    {
        // Arrange
        var unauthorizedAgent = $"unauthorized-agent-{Guid.NewGuid()}";
        var knowledge = await CreateTestKnowledgeAsync("test-knowledge", unauthorizedAgent, "Test content");

        // Act
        var response = await DeleteAsync($"/api/client/knowledge/{knowledge.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAllVersions_WithValidRequest_DeletesAllVersions()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var knowledge1 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Version 1");
        var knowledge2 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Version 2");
        
        var request = new DeleteAllVersionsRequest
        {
            Name = "test-knowledge",
            Agent = agentName
        };

        // Act
        var response = await DeleteAsJsonAsync("/api/client/knowledge/all", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Verify deletion
        var getResponse1 = await GetAsync($"/api/client/knowledge/{knowledge1.Id}");
        var getResponse2 = await GetAsync($"/api/client/knowledge/{knowledge2.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse1.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse2.StatusCode);
    }

    [Fact]
    public async Task DeleteAllVersions_WithUnauthorizedAgent_ReturnsForbidden()
    {
        // Arrange
        var unauthorizedAgent = $"unauthorized-agent-{Guid.NewGuid()}";
        var request = new DeleteAllVersionsRequest
        {
            Name = "test-knowledge",
            Agent = unauthorizedAgent
        };

        // Act
        var response = await DeleteAsJsonAsync("/api/client/knowledge/all", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetVersions_WithValidNameAndAgent_ReturnsAllVersions()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var knowledge1 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Version 1", createdAt: DateTime.UtcNow.AddHours(-2));
        var knowledge2 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Version 2", createdAt: DateTime.UtcNow.AddHours(-1));
        var knowledge3 = await CreateTestKnowledgeAsync("test-knowledge", agentName, "Version 3");

        // Act
        var response = await GetAsync($"/api/client/knowledge/versions?name=test-knowledge&agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge[]>(response);
        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        
        // Verify they are ordered by creation time (most recent first)
        Assert.Equal("Version 3", result[0].Content);

    }

    [Fact]
    public async Task GetVersions_WithNonExistentName_ReturnsEmptyArray()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);

        // Act
        var response = await GetAsync($"/api/client/knowledge/versions?name=non-existent&agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Knowledge[]>(response);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLatestAll_WithMultipleAgents_ReturnsOnlyAuthorizedKnowledge()
    {
        // Arrange
        var agentName1 = $"test-agent-{Guid.NewGuid()}";
        var agentName2 = $"other-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName1);
        await CreateTestAgentAsync(agentName2);
        
        // Create knowledge for authorized agent
        await CreateTestKnowledgeAsync("authorized-knowledge", agentName1, "Authorized content");
        
        // Create knowledge for agent2 (should not be returned when querying for agent1)
        await CreateTestKnowledgeAsync("other-agent-knowledge", agentName2, "Other content");

        // Act - query for agent1 only
        var response = await GetAsync($"/api/client/knowledge/latest/all?agent={agentName1}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var groupedResponse = await ReadAsJsonAsync<GroupedKnowledgeResponse>(response);
        Assert.NotNull(groupedResponse);
        
        var groupNames = groupedResponse.Groups.Select(g => g.Name).ToList();
        Assert.Contains("authorized-knowledge", groupNames);
        Assert.DoesNotContain("other-agent-knowledge", groupNames);
    }

    [Fact]
    public async Task Create_WithMultipleVersions_CreatesUniqueVersions()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);
        var request1 = new KnowledgeRequest
        {
            Name = "versioned-knowledge",
            Content = "Version 1 content",
            Type = "text",
            Agent = agentName
        };
        var request2 = new KnowledgeRequest
        {
            Name = "versioned-knowledge",
            Content = "Version 2 content",
            Type = "text",
            Agent = agentName
        };

        // Act
        var response1 = await PostAsJsonAsync("/api/client/knowledge/", request1);
        var response2 = await PostAsJsonAsync("/api/client/knowledge/", request2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        
        var result1 = await ReadAsJsonAsync<Knowledge>(response1);
        var result2 = await ReadAsJsonAsync<Knowledge>(response2);
        
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotEqual(result1.Version, result2.Version);
        Assert.NotEqual(result1.Id, result2.Id);
        Assert.Equal("versioned-knowledge", result1.Name);
        Assert.Equal("versioned-knowledge", result2.Name);
    }

    private async Task<Agent> CreateTestAgentAsync(string agentName)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        
        // Check if agent already exists to avoid duplicate key errors
        var existingAgent = await agentRepository.GetByNameAsync(agentName, TestTenantId, TestUserId, []);
        if (existingAgent != null)
        {
            return existingAgent;
        }
        
        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = [TestUserId],
            ReadAccess = [TestUserId],
            WriteAccess = [TestUserId],
            CreatedBy = TestUserId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    private async Task<Knowledge> CreateTestKnowledgeAsync(
        string name, 
        string agentName, 
        string content,
        string type = "text",
        DateTime? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var knowledgeRepository = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();
        
        var knowledge = new Knowledge
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Content = content,
            Type = type,
            Agent = agentName,
            TenantId = TestTenantId,
            CreatedBy = TestUserId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Version = ObjectId.GenerateNewId().ToString() // Simple version for testing
        };

        await knowledgeRepository.CreateAsync(knowledge);
        return knowledge;
    }

    protected async Task<HttpResponseMessage> DeleteAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Delete, requestUri)
        {
            Content = content
        };
        
        return await _client.SendAsync(request);
    }
} 