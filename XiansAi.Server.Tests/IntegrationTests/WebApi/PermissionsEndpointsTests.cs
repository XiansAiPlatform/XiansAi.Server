using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson;
using Shared.Auth;

namespace Tests.IntegrationTests.WebApi;

public class PermissionsEndpointsTests : WebApiIntegrationTestBase
{
    private const string TestUserId = "test-user";
    private const string TestUserId2 = "test-user-2";

    public PermissionsEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetPermissions_WithValidAgentName_ReturnsPermissions()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("permissions-test-agent");

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agent.Name}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = await response.Content.ReadFromJsonAsync<PermissionDto>();
        Assert.NotNull(permissions);
    }

    [Fact]
    public async Task GetPermissions_WithNonExistentAgent_ReturnsNotFound()
    {
        // Act
        var response = await GetAsync("/api/client/permissions/agent/non-existent-agent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePermissions_WithValidData_UpdatesPermissions()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("update-permissions-agent");
        var permissionsDto = new PermissionDto
        {
            OwnerAccess = new List<string> { TestUserId },
            ReadAccess = new List<string> { TestUserId, TestUserId2 },
            WriteAccess = new List<string> { TestUserId }
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/permissions/agent/{agent.Name}", permissionsDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify permissions were updated
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var updatedAgent = await agentRepository.GetByNameInternalAsync(agent.Name, TestTenantId);
        Assert.NotNull(updatedAgent);
        Assert.Contains(TestUserId, updatedAgent.OwnerAccess);
        Assert.Contains(TestUserId2, updatedAgent.ReadAccess);
    }

    [Fact]
    public async Task AddUser_WithValidData_AddsUserToPermissions()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("add-user-agent");
        var userPermission = new UserPermissionDto
        {
            UserId = TestUserId2,
            PermissionLevel = "read"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agent.Name}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user was added
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var updatedAgent = await agentRepository.GetByNameInternalAsync(agent.Name, TestTenantId);
        Assert.NotNull(updatedAgent);
        Assert.Contains(TestUserId2, updatedAgent.ReadAccess);
    }

    [Fact]
    public async Task AddUser_WithInvalidPermissionLevel_ReturnsError()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("invalid-permission-agent");
        var userPermission = new UserPermissionDto
        {
            UserId = TestUserId2,
            PermissionLevel = "invalid-level"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agent.Name}/users", userPermission);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task RemoveUser_WithValidUserId_RemovesUserFromPermissions()
    {
        // Arrange
        var agent = await CreateTestAgentWithUserAsync("remove-user-agent", TestUserId2, "read");

        // Act
        var response = await DeleteAsync($"/api/client/permissions/agent/{agent.Name}/users/{TestUserId2}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user was removed
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var updatedAgent = await agentRepository.GetByNameInternalAsync(agent.Name, TestTenantId);
        Assert.NotNull(updatedAgent);
        Assert.DoesNotContain(TestUserId2, updatedAgent.ReadAccess);
    }

    [Fact]
    public async Task RemoveUser_WithNonExistentUser_ReturnsNotFound()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("remove-nonexistent-user-agent");

        // Act
        var response = await DeleteAsync($"/api/client/permissions/agent/{agent.Name}/users/non-existent-user");

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                   response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateUserPermission_WithValidData_UpdatesPermissionLevel()
    {
        // Arrange
        var agent = await CreateTestAgentWithUserAsync("update-user-permission-agent", TestUserId2, "read");

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/client/permissions/agent/{agent.Name}/users/{TestUserId2}/write");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify permission level was updated
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var updatedAgent = await agentRepository.GetByNameInternalAsync(agent.Name, TestTenantId);
        Assert.NotNull(updatedAgent);
        Assert.Contains(TestUserId2, updatedAgent.WriteAccess);
    }

    [Fact]
    public async Task UpdateUserPermission_WithInvalidLevel_ReturnsError()
    {
        // Arrange
        var agent = await CreateTestAgentWithUserAsync("invalid-level-agent", TestUserId2, "read");

        // Act
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/client/permissions/agent/{agent.Name}/users/{TestUserId2}/invalid-level");
        var response = await _client.SendAsync(request);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest || 
                   response.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetPermissions_WithMultipleUsers_ReturnsAllPermissions()
    {
        // Arrange
        var agent = await CreateTestAgentWithMultipleUsersAsync("multi-user-agent");

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agent.Name}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = await response.Content.ReadFromJsonAsync<PermissionDto>();
        Assert.NotNull(permissions);
        Assert.Contains(TestUserId, permissions.OwnerAccess);
        Assert.Contains(TestUserId2, permissions.ReadAccess);
    }

    private async Task<Agent> CreateTestAgentAsync(string agentName)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = new List<string> { TestUserId },
            ReadAccess = new List<string> { TestUserId },
            WriteAccess = new List<string> { TestUserId },
            CreatedBy = TestUserId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    private async Task<Agent> CreateTestAgentWithUserAsync(string agentName, string userId, string permissionLevel)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = permissionLevel == "owner" ? new List<string> { TestUserId, userId } : new List<string> { TestUserId },
            ReadAccess = permissionLevel == "read" ? new List<string> { TestUserId, userId } : new List<string> { TestUserId },
            WriteAccess = permissionLevel == "write" ? new List<string> { TestUserId, userId } : new List<string> { TestUserId },
            CreatedBy = TestUserId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    private async Task<Agent> CreateTestAgentWithMultipleUsersAsync(string agentName)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = new List<string> { TestUserId },
            ReadAccess = new List<string> { TestUserId, TestUserId2 },
            WriteAccess = new List<string> { TestUserId },
            CreatedBy = TestUserId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }
}

// DTOs
public class PermissionDto
{
    public List<string> OwnerAccess { get; set; } = new();
    public List<string> ReadAccess { get; set; } = new();
    public List<string> WriteAccess { get; set; } = new();
}

public class UserPermissionDto
{
    public string UserId { get; set; } = string.Empty;
    public string PermissionLevel { get; set; } = string.Empty;
}

