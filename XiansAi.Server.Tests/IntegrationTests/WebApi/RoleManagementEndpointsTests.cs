using System.Net;
using System.Net.Http.Json;
using Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson;
using Shared.Auth;

namespace Tests.IntegrationTests.WebApi;

public class RoleManagementEndpointsTests : WebApiIntegrationTestBase
{
    public RoleManagementEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetCurrentUserRoles_ReturnsRoles()
    {
        // Act
        var response = await GetAsync("/api/roles/current");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var roles = await response.Content.ReadFromJsonAsync<List<string>>();
        Assert.NotNull(roles);
    }

    [Fact]
    public async Task GetAllTenantAdmins_WithValidTenantId_ReturnsAdmins()
    {
        // Arrange
        var user = await CreateTestUserAsync("admin-test-user", "admin@example.com");
        await AssignTenantAdminRoleAsync(user.Id, TestTenantId);

        // Act
        var response = await GetAsync($"/api/roles/tenant/{TestTenantId}/admins");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var admins = await response.Content.ReadFromJsonAsync<List<UserRoleInfo>>();
        Assert.NotNull(admins);
        Assert.True(admins.Count > 0);
    }

    [Fact]
    public async Task GetAllTenantAdmins_WithNoAdmins_ReturnsEmptyList()
    {
        // Arrange - a fresh tenant with no admins
        var newTenantId = "tenant-" + Guid.NewGuid().ToString();

        // Act
        var response = await GetAsync($"/api/roles/tenant/{newTenantId}/admins");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var admins = await response.Content.ReadFromJsonAsync<List<UserRoleInfo>>();
        Assert.NotNull(admins);
        Assert.Empty(admins);
    }

    [Fact]
    public async Task AssignTenantAdmin_WithValidData_AssignsRole()
    {
        // Arrange
        var user = await CreateTestUserAsync("new-admin-user", "newadmin@example.com");
        var roleDto = new RoleDto
        {
            UserId = user.UserId,
            Role = SystemRoles.TenantAdmin
        };

        // Act
        var response = await PostAsJsonAsync($"/api/roles/tenant/{TestTenantId}/admins", roleDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var updatedUser = await userRepository.GetByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Contains(updatedUser.TenantRoles,
            tr => tr.Tenant == TestTenantId && tr.Roles.Contains(SystemRoles.TenantAdmin));
    }

    [Fact]
    public async Task RemoveRoleFromUser_WithValidData_RemovesRole()
    {
        // Arrange
        var user = await CreateTestUserAsync("remove-role-user", "removerole@example.com");
        await AssignTenantAdminRoleAsync(user.Id, TestTenantId);

        // Act
        var response = await DeleteAsync($"/api/roles/tenant/{TestTenantId}/admins/{user.UserId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var updatedUser = await userRepository.GetByIdAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.DoesNotContain(updatedUser.TenantRoles,
            tr => tr.Tenant == TestTenantId && tr.Roles.Contains(SystemRoles.TenantAdmin));
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

    private async Task AssignTenantAdminRoleAsync(string userId, string tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = await userRepository.GetByIdAsync(userId);
        if (user != null)
        {
            user.TenantRoles ??= new List<TenantRole>();
            var tenantRole = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tenantRole == null)
            {
                user.TenantRoles.Add(new TenantRole
                {
                    Tenant = tenantId,
                    Roles = new List<string> { SystemRoles.TenantAdmin },
                    IsApproved = true
                });
            }
            else if (!tenantRole.Roles.Contains(SystemRoles.TenantAdmin))
            {
                tenantRole.Roles.Add(SystemRoles.TenantAdmin);
            }
            user.UpdatedAt = DateTime.UtcNow;
            await userRepository.UpdateAsyncById(user.Id, user);
        }
    }
}

// DTOs
public class RoleDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class UserRoleInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}
