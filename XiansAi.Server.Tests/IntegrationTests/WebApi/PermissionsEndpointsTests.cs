using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Shared.Repositories;
using Shared.Services;
using Shared.Data;
using Shared.Data.Models;
using System.Net.Http.Json;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class PermissionsEndpointsTests : WebApiIntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private const string TestUserId = "test-user";
    private const string TestAgentName = "test-agent";
    private const string NonExistentAgentName = "non-existent-agent";

    public PermissionsEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    #region GetPermissions Tests

    [Fact]
    public async Task GetPermissions_WithValidAgentName_ReturnsPermissions()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, new[] { "write-user" }, new[] { "read-user" });

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var permissions = await ReadAsJsonAsync<PermissionDto>(response);
        Assert.NotNull(permissions);
        Assert.NotNull(permissions.OwnerAccess);
        Assert.NotNull(permissions.ReadAccess);
        Assert.NotNull(permissions.WriteAccess);
    }

    [Fact]
    public async Task GetPermissions_WithNonExistentAgent_ReturnsNotFound()
    {
        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{NonExistentAgentName}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPermissions_WithEmptyAgentName_ReturnsBadRequest()
    {
        // Act
        var response = await GetAsync("/api/client/permissions/agent/");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // Route not found
    }

    [Fact]
    public async Task GetPermissions_WithWhitespaceAgentName_ReturnsNotFound()
    {
        // Act
        var response = await GetAsync("/api/client/permissions/agent/   ");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region UpdatePermissions Tests

    [Fact]
    public async Task UpdatePermissions_WithValidPermissions_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var permissions = new PermissionDto
        {
            OwnerAccess = new List<string> { TestUserId },
            ReadAccess = new List<string> { "user1", "user2" },
            WriteAccess = new List<string> { "user3" }
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/permissions/agent/{agentName}", permissions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdatePermissions_WithNonExistentAgent_ReturnsOK()
    {
        // Arrange
        var permissions = new PermissionDto
        {
            OwnerAccess = new List<string> { TestUserId },
            ReadAccess = new List<string> { "user1" },
            WriteAccess = new List<string> { "user2" }
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/permissions/agent/{NonExistentAgentName}", permissions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // API creates permissions even for non-existent agents
    }

    [Fact]
    public async Task UpdatePermissions_WithNullPermissions_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await PutAsJsonAsync($"/api/client/permissions/agent/{agentName}", (PermissionDto?)null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePermissions_WithInvalidPermissionsStructure_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var permissions = new PermissionDto
        {
            OwnerAccess = null!, // Invalid - null list
            ReadAccess = new List<string> { "user1" },
            WriteAccess = new List<string> { "user2" }
        };

        // Act
        var response = await PutAsJsonAsync($"/api/client/permissions/agent/{agentName}", permissions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region AddUser Tests

    [Fact]
    public async Task AddUser_WithValidUserAndPermission_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "new-user-id",
            PermissionLevel = "Read"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task AddUser_WithOwnerPermission_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "new-owner-id",
            PermissionLevel = "Owner"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task AddUser_WithWritePermission_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "new-writer-id",
            PermissionLevel = "Write"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task AddUser_WithInvalidPermissionLevel_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "test-user",
            PermissionLevel = "InvalidLevel"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddUser_WithEmptyUserId_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "",
            PermissionLevel = "Read"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddUser_WithEmptyPermissionLevel_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        var userPermission = new UserPermissionDto
        {
            UserId = "test-user",
            PermissionLevel = ""
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{agentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddUser_ToNonExistentAgent_ReturnsOK()
    {
        // Arrange
        var userPermission = new UserPermissionDto
        {
            UserId = "test-user",
            PermissionLevel = "Read"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/client/permissions/agent/{NonExistentAgentName}/users", userPermission);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // API creates permissions even for non-existent agents
    }

    #endregion

    #region RemoveUser Tests

    [Fact]
    public async Task RemoveUser_WithValidUserId_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, new[] { "user-to-remove" }, Array.Empty<string>());

        // Act
        var response = await DeleteAsync($"/api/client/permissions/agent/{agentName}/users/user-to-remove");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveUser_WithNonExistentUser_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await DeleteAsync($"/api/client/permissions/agent/{agentName}/users/non-existent-user");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task RemoveUser_FromNonExistentAgent_ReturnsOK()
    {
        // Act
        var response = await DeleteAsync($"/api/client/permissions/agent/{NonExistentAgentName}/users/test-user");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // API handles gracefully
    }

    [Fact]
    public async Task RemoveUser_WithInvalidRoute_ReturnsMethodNotAllowed()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act - Call with malformed URL (empty userId causes routing issue)
        var response = await DeleteAsync($"/api/client/permissions/agent/{agentName}/users/ ");

        // Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode); // Routing returns MethodNotAllowed for malformed URL
    }

    #endregion

    #region UpdateUserPermission Tests

    [Fact]
    public async Task UpdateUserPermission_WithValidParameters_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        // Make test-user the owner so they can modify permissions
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act - Update permission for a different user
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{agentName}/users/other-user/Read"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateUserPermission_ToOwnerLevel_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        // Make test-user the owner so they can modify permissions
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act - Update permission for a different user
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{agentName}/users/other-user/Owner"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateUserPermission_ToReadLevel_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        // Make test-user the owner so they can modify permissions
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act - Update permission for a different user
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{agentName}/users/other-user/Read"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    [Fact]
    public async Task UpdateUserPermission_WithInvalidPermissionLevel_ReturnsBadRequest()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, new[] { "test-user" }, Array.Empty<string>());

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{agentName}/users/test-user/InvalidLevel"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserPermission_ForNonExistentAgent_ReturnsOK()
    {
        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{NonExistentAgentName}/users/test-user/Read"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // API handles gracefully
    }

    [Fact]
    public async Task UpdateUserPermission_ForNonExistentUser_ReturnsOK()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, 
            $"/api/client/permissions/agent/{agentName}/users/non-existent-user/Read"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<bool>(response);
        Assert.True(result);
    }

    #endregion

    #region Permission Check Tests

    [Fact]
    public async Task CheckReadPermission_WithReadAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), Array.Empty<string>(), new[] { TestUserId });

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/read");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckReadPermission_WithWriteAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), new[] { TestUserId }, Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/read");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckReadPermission_WithOwnerAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/read");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckReadPermission_WithoutAccess_ReturnsFalse()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/read");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task CheckWritePermission_WithWriteAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), new[] { TestUserId }, Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/write");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckWritePermission_WithOwnerAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/write");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckWritePermission_WithOnlyReadAccess_ReturnsFalse()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), Array.Empty<string>(), new[] { TestUserId });

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/write");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task CheckOwnerPermission_WithOwnerAccess_ReturnsTrue()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, new[] { TestUserId }, Array.Empty<string>(), Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/owner");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.True(hasPermission);
    }

    [Fact]
    public async Task CheckOwnerPermission_WithWriteAccess_ReturnsFalse()
    {
        // Arrange
        var agentName = $"test-agent-{Guid.NewGuid()}";
        await CreateTestAgentWithPermissionsAsync(agentName, Array.Empty<string>(), new[] { TestUserId }, Array.Empty<string>());

        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{agentName}/check/owner");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var hasPermission = await ReadAsJsonAsync<bool>(response);
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task CheckPermission_ForNonExistentAgent_ReturnsNotFound()
    {
        // Act
        var response = await GetAsync($"/api/client/permissions/agent/{NonExistentAgentName}/check/read");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region Helper Methods

    private async Task<Agent> CreateTestAgentWithPermissionsAsync(
        string agentName,
        string[] ownerUsers,
        string[] writeUsers,
        string[] readUsers)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = TestUserId,
            Permissions = new Permission
            {
                OwnerAccess = ownerUsers?.ToList() ?? new List<string>(),
                WriteAccess = writeUsers?.ToList() ?? new List<string>(),
                ReadAccess = readUsers?.ToList() ?? new List<string>()
            }
        };

        // Insert directly into database
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<Agent>("agents");
        await collection.InsertOneAsync(agent);

        return agent;
    }

    private async Task<Agent?> GetAgentByNameAsync(string agentName)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<Agent>("agents");
        
        return await collection.Find(a => a.Name == agentName && a.Tenant == TestTenantId).FirstOrDefaultAsync();
    }

    #endregion
} 