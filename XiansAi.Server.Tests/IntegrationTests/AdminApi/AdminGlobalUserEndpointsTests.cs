using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
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

    [Fact]
    public async Task ListAndGetGlobalUser_ReturnsCreatedUser()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var user = await CreateGlobalTestUserAsync($"listed-{Guid.NewGuid()}@example.com");

        var list = await GetAsync(
            $"/api/v1/admin/users?page=1&pageSize=20&search={Uri.EscapeDataString(user.Email)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await ReadAsJsonAsync<GlobalUserListResult>(list);
        Assert.Contains(listed!.Users, item => item.UserId == user.UserId);

        var get = await GetAsync($"/api/v1/admin/users/{user.UserId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = await ReadAsJsonAsync<GlobalUserDetail>(get);
        Assert.Equal(user.Email, detail!.Email);
    }

    [Fact]
    public async Task PatchGlobalUser_UpdatesNameAndEmail()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var user = await CreateGlobalTestUserAsync($"patch-{Guid.NewGuid()}@example.com");

        var response = await PatchAsJsonAsync($"/api/v1/admin/users/{user.UserId}", new
        {
            name = "Patched Name",
            email = $"patched-{Guid.NewGuid()}@example.com"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsJsonAsync<GlobalUserDetail>(response);
        Assert.Equal("Patched Name", updated!.Name);
        Assert.StartsWith("patched-", updated.Email);
    }

    [Fact]
    public async Task SetSysAdminAndStatus_UpdatesFlags()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var user = await CreateGlobalTestUserAsync($"flags-{Guid.NewGuid()}@example.com");

        var grant = await PutAsJsonAsync($"/api/v1/admin/users/{user.UserId}/sysadmin", new { isSysAdmin = true });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var granted = await ReadAsJsonAsync<GlobalUserDetail>(grant);
        Assert.True(granted!.IsSysAdmin);

        var disable = await PutAsJsonAsync($"/api/v1/admin/users/{user.UserId}/status", new
        {
            enabled = false,
            reason = "integration-test"
        });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        var disabled = await ReadAsJsonAsync<GlobalUserDetail>(disable);
        Assert.True(disabled!.IsLockedOut);
        Assert.False(disabled.IsEnabled);
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
