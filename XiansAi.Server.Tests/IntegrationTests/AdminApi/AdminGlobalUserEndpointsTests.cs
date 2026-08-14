using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminGlobalUserEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminGlobalUserEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task DeleteGlobalUser_WithValidUserId_DeletesUser()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var user = await CreateGlobalTestUserAsync("delete-me@example.com");

        var response = await DeleteAsync($"/api/v1/admin/users/{user.UserId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var deleted = await userRepository.GetByUserIdAsync(user.UserId);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteGlobalUser_WithUnknownUser_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await DeleteAsync($"/api/v1/admin/users/unknown-user-{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteGlobalUser_WhenDeletingSelf_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await DeleteAsync($"/api/v1/admin/users/{_adminUserId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var stillPresent = await userRepository.GetByUserIdAsync(_adminUserId!);
        Assert.NotNull(stillPresent);
    }

    [Fact]
    public async Task DeleteGlobalUser_AsTenantAdmin_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);
        var user = await CreateGlobalTestUserAsync("tenant-admin-cannot-delete@example.com");

        var response = await DeleteAsync($"/api/v1/admin/users/{user.UserId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var stillPresent = await userRepository.GetByUserIdAsync(user.UserId);
        Assert.NotNull(stillPresent);
    }

    private async Task<User> CreateGlobalTestUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = ObjectId.GenerateNewId().ToString(),
            Email = email,
            Name = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>(),
        };

        await userRepository.CreateAsync(user);
        return user;
    }
}
