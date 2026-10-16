using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminApiKeyEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminApiKeyEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateListGetRotateAndRevoke_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = _adminUserId!;

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}",
            new { name = $"key-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();
        var rawKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrEmpty(id));
        Assert.StartsWith("sk-Xnai-", rawKey);

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(id!, await list.Content.ReadAsStringAsync());

        var get = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var rotate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}/rotate?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using var rotated = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
        var rotatedKey = rotated.RootElement.GetProperty("apiKey").GetString();
        Assert.StartsWith("sk-Xnai-", rotatedKey);
        Assert.NotEqual(rawKey, rotatedKey);

        var revoke = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}/revoke?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var getAfterRevoke = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_AsTenantAdmin_ForAnotherUser_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var other = await CreateTestUserWithRoleAsync($"other-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(other.UserId)}",
            new { name = "not-allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_NamedByEmail_StoresTheUserId_AndTheKeyAuthenticates()
    {
        // Issue #583: a SysAdmin whose user id is not their email created a key by email, and the
        // key then failed auth because roles are looked up by the stored created_by.
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = _adminUserId!;
        var email = $"{userId}@test.com";

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(email)}",
            new { name = $"key-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal(userId, created.RootElement.GetProperty("createdBy").GetString());

        _adminApiKey = created.RootElement.GetProperty("apiKey").GetString();
        ConfigureClientWithAuth(_client.HttpClient);

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task LegacyKeyStoredWithEmail_IsListedAndRevocable_ByUserId()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = _adminUserId!;

        var legacyKeyId = await CreateKeyInMongoAsync(tenantId, $"{userId}@test.com");

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(legacyKeyId, await list.Content.ReadAsStringAsync());

        var revoke = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{legacyKeyId}/revoke?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_AsTenantAdmin_NamingThemselvesByEmail_Succeeds()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        var email = $"{_adminUserId}@test.com";

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(email)}",
            new { name = $"key-{Guid.NewGuid()}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_adminUserId, created.RootElement.GetProperty("createdBy").GetString());
    }

    [Fact]
    public async Task CreateApiKey_AsTenantAdmin_ForAnotherUserByEmail_ReturnsForbidden()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var other = await CreateTestUserWithRoleAsync($"other-{Guid.NewGuid()}", tenantId, SystemRoles.TenantUser);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(other.Email)}",
            new { name = "not-allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LegacyKeyStoredWithEmail_IsHidden_WhenAnotherAccountHoldsTheEmail()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = _adminUserId!;
        var sharedEmail = $"{userId}@test.com";
        await CreateUserWithEmailAsync($"second-account-{Guid.NewGuid()}", sharedEmail);
        var legacyKeyId = await CreateKeyInMongoAsync(tenantId, sharedEmail);

        var list = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(userId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(legacyKeyId, await list.Content.ReadAsStringAsync());

        var revoke = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{legacyKeyId}/revoke?userId={Uri.EscapeDataString(userId)}",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_AsSysAdmin_ForAnotherUserByEmail_IssuesAKeyActingAsThatUser()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var other = await CreateTestUserWithRoleAsync($"other-{Guid.NewGuid()}", tenantId, SystemRoles.TenantAdmin);

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(other.Email)}",
            new { name = $"key-{Guid.NewGuid()}" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal(other.UserId, created.RootElement.GetProperty("createdBy").GetString());

        // The key carries the other user's TenantAdmin role, not the SysAdmin's, so it may only
        // manage that user's keys.
        using var otherClient = CreateClientWithKey(created.RootElement.GetProperty("apiKey").GetString()!);
        var ownList = await otherClient.GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(other.UserId)}");
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        var adminList = await otherClient.GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={Uri.EscapeDataString(_adminUserId!)}");
        Assert.Equal(HttpStatusCode.Forbidden, adminList.StatusCode);
    }

    [Fact]
    public async Task TenantAdmin_CannotReachAnotherUsersKey_ByItsId()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        var other = await CreateTestUserWithRoleAsync($"other-{Guid.NewGuid()}", tenantId, SystemRoles.TenantAdmin);
        var otherKeyId = await CreateKeyInMongoAsync(tenantId, other.UserId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        var self = Uri.EscapeDataString(_adminUserId!);

        var get = await GetAsync($"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{otherKeyId}?userId={self}");
        var rotate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{otherKeyId}/rotate?userId={self}", new { });
        var revoke = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{otherKeyId}/revoke?userId={self}", new { });

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rotate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [Fact]
    public async Task RotateApiKey_InvalidatesTheOldKey_AndKeepsTheOwner()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var userId = Uri.EscapeDataString(_adminUserId!);

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={userId}",
            new { name = $"key-{Guid.NewGuid()}" });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();
        var oldKey = created.RootElement.GetProperty("apiKey").GetString()!;

        var rotate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{id}/rotate?userId={userId}", new { });
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        using var rotated = JsonDocument.Parse(await rotate.Content.ReadAsStringAsync());
        Assert.Equal(_adminUserId, rotated.RootElement.GetProperty("createdBy").GetString());
        var newKey = rotated.RootElement.GetProperty("apiKey").GetString()!;

        var listPath = $"/api/v1/admin/tenants/{tenantId}/admin-apikeys?userId={userId}";
        using var oldClient = CreateClientWithKey(oldKey);
        using var newClient = CreateClientWithKey(newKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync(listPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync(listPath)).StatusCode);
    }

    [Fact]
    public async Task GetApiKey_WithUnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/admin-apikeys/{MongoDB.Bson.ObjectId.GenerateNewId()}?userId={Uri.EscapeDataString(_adminUserId!)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> CreateKeyInMongoAsync(string tenantId, string createdBy)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var (_, meta) = await repository.CreateAsync(tenantId, $"seeded-{Guid.NewGuid()}", createdBy);
        return meta.Id;
    }

    private async Task CreateUserWithEmailAsync(string userId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var created = await users.CreateAsync(new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            Email = email,
            Name = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            TenantRoles = new List<TenantRole>()
        });
        Assert.True(created);
    }

    private HttpClient CreateClientWithKey(string apiKey)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }
}
