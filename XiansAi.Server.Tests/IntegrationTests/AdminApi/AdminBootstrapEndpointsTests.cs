using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Integration tests for the anonymous platform bootstrap endpoint.
/// The endpoint only succeeds while no users exist, so each test clears the relevant
/// collections to control the precondition.
/// </summary>
public class AdminBootstrapEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminBootstrapEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    /// <summary>
    /// Removes all users, tenants and API keys so the bootstrap "no users exist" gate is open.
    /// </summary>
    private void ClearBootstrapCollections()
    {
        var empty = Builders<BsonDocument>.Filter.Empty;
        _database.GetCollection<BsonDocument>("users").DeleteMany(empty);
        _database.GetCollection<BsonDocument>("tenants").DeleteMany(empty);
        _database.GetCollection<BsonDocument>("api_keys").DeleteMany(empty);
    }

    private class BootstrapResponseDto
    {
        public string? ApiKey { get; set; }
        public string? TenantId { get; set; }
        public string? UserId { get; set; }
    }

    [Fact]
    public async Task Bootstrap_OnEmptyDatabase_CreatesSysAdminAndReturnsApiKey()
    {
        // Arrange
        ClearBootstrapCollections();
        var email = "bootstrap-admin@example.com";

        // Act
        var response = await GetAsync($"/api/v1/admin/bootstrap?email={Uri.EscapeDataString(email)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsJsonAsync<BootstrapResponseDto>(response);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result!.ApiKey));
        Assert.StartsWith("sk-Xnai-", result.ApiKey);
        Assert.Equal("default", result.TenantId);
        Assert.Equal(email, result.UserId);

        // The created user must be a SysAdmin and the tenant enabled
        using var scope = _factory.Services.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();

        var createdUser = await userRepository.GetByUserIdAsync(email);
        Assert.NotNull(createdUser);
        Assert.True(createdUser!.IsSysAdmin);

        var createdTenant = await tenantRepository.GetByTenantIdAsync("default");
        Assert.NotNull(createdTenant);
        Assert.True(createdTenant.Enabled);
    }

    [Fact]
    public async Task Bootstrap_WithCustomTenantId_UsesProvidedTenant()
    {
        // Arrange
        ClearBootstrapCollections();
        var email = "custom-tenant-admin@example.com";
        var tenantId = "acme";

        // Act
        var response = await GetAsync(
            $"/api/v1/admin/bootstrap?email={Uri.EscapeDataString(email)}&tenantId={tenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsJsonAsync<BootstrapResponseDto>(response);
        Assert.NotNull(result);
        Assert.Equal(tenantId, result!.TenantId);
        Assert.False(string.IsNullOrEmpty(result.ApiKey));
    }

    [Fact]
    public async Task Bootstrap_WhenUsersAlreadyExist_ReturnsConflict()
    {
        // Arrange: first bootstrap succeeds and creates a user
        ClearBootstrapCollections();
        var firstResponse = await GetAsync("/api/v1/admin/bootstrap?email=first-admin@example.com");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Act: second bootstrap should be rejected because a user now exists
        var secondResponse = await GetAsync("/api/v1/admin/bootstrap?email=second-admin@example.com");

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_WithMissingEmail_ReturnsBadRequest()
    {
        // Arrange
        ClearBootstrapCollections();

        // Act
        var response = await GetAsync("/api/v1/admin/bootstrap");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_WithInvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        ClearBootstrapCollections();

        // Act
        var response = await GetAsync("/api/v1/admin/bootstrap?email=not-an-email");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
