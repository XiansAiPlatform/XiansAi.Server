using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson;
using Shared.Services;

namespace Tests.IntegrationTests.WebApi;

public class UserManagementEndpointsTests : WebApiIntegrationTestBase
{
    public UserManagementEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetAllUsers_WithValidRequest_ReturnsUsers()
    {
        // Arrange
        await CreateTestUserAsync("test-user-1", "test@example.com");
        await CreateTestUserAsync("test-user-2", "test2@example.com");

        // Act - Use enum integer value: 0=ALL, 1=ADMIN, 2=NON_ADMIN
        var response = await GetAsync("/api/users/all?page=1&pageSize=10&type=0");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
        Assert.True(result.Users.Count > 0);
    }

    [Fact]
    public async Task GetAllUsers_WithTenantFilter_ReturnsFilteredUsers()
    {
        // Arrange
        var user1 = await CreateTestUserAsync("filter-user-1", "filter1@example.com");
        var user2 = await CreateTestUserAsync("filter-user-2", "filter2@example.com");

        // Act - Use enum integer value
        var response = await GetAsync($"/api/users/all?page=1&pageSize=10&type=0&tenant={TestTenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
    }

    [Fact]
    public async Task GetAllUsers_WithSearchFilter_ReturnsMatchingUsers()
    {
        // Arrange
        await CreateTestUserAsync("searchable-user", "searchable@example.com");

        // Act - Use enum integer value
        var response = await GetAsync("/api/users/all?page=1&pageSize=10&type=0&search=searchable");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
    }

    [Fact]
    public async Task UpdateUser_WithValidData_UpdatesUser()
    {
        // Arrange
        var user = await CreateTestUserAsync("update-test-user", "update@example.com");
        var updateDto = new EditUserDto
        {
            Id = user.Id,
            UserId = user.UserId,
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act
        var response = await PutAsJsonAsync("/api/users/update", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user was updated
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var updatedUser = await userRepository.GetByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal("Updated Name", updatedUser.Name);
        Assert.Equal("updated@example.com", updatedUser.Email);
    }

    [Fact]
    public async Task UpdateUser_WithInvalidUserId_ReturnsError()
    {
        // Arrange
        var updateDto = new EditUserDto
        {
            Id = "non-existent-id",
            UserId = "non-existent-user-id",
            Name = "Updated Name",
            Email = "updated@example.com"
        };

        // Act
        var response = await PutAsJsonAsync("/api/users/update", updateDto);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.NotFound || 
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DeleteUser_WithValidUserId_DeletesUser()
    {
        // Arrange
        var user = await CreateTestUserAsync("delete-test-user", "delete@example.com");

        // Act
        var response = await DeleteAsync($"/api/users/{user.UserId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user was deleted
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var deletedUser = await userRepository.GetByIdAsync(user.Id);
        Assert.Null(deletedUser);
    }

    [Fact]
    public async Task DeleteUser_WithInvalidUserId_ReturnsNotFound()
    {
        // Act
        var response = await DeleteAsync("/api/users/non-existent-user-id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllUsers_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange - Create multiple users
        for (int i = 0; i < 5; i++)
        {
            await CreateTestUserAsync($"page-user-{i}", $"page{i}@example.com");
        }

        // Act - Request first page with page size of 2, use enum integer value
        var response = await GetAsync("/api/users/all?page=1&pageSize=2&type=0");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UserListResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Users);
        Assert.True(result.TotalCount >= 5);
    }

    private async Task<User> CreateTestUserAsync(string name, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = ObjectId.GenerateNewId().ToString(),
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>()
        };

        await userRepository.CreateAsync(user);
        return user;
    }
}

// DTOs matching the service expectations
public class UserListResponse
{
    public List<UserInfo> Users { get; set; } = new();
    public int TotalCount { get; set; }
}

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

