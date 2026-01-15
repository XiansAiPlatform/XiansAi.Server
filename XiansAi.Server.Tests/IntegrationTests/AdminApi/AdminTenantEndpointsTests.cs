using System.Net;
using Shared.Data.Models;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminTenantEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminTenantEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListTenants_WithValidRequest_ReturnsTenantList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        await CreateTestTenantAsync(tenantId);
        await CreateTestTenantAsync($"tenant-2-{Guid.NewGuid()}");

        // Act
        var response = await GetAsync("/api/v1/admin/tenants");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
    }

    [Fact]
    public async Task GetTenantByTenantId_WithValidId_ReturnsTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var tenant = await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(tenant.TenantId, result.TenantId);
    }

    [Fact]
    public async Task GetTenantByTenantId_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var invalidTenantId = $"non-existent-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{invalidTenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTenant_WithValidRequest_CreatesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var request = new
        {
            tenantId = $"new-tenant-{Guid.NewGuid()}",
            name = "New Test Tenant",
            domain = $"new-tenant-{Guid.NewGuid()}.test.com"
        };

        // Act
        var response = await PostAsJsonAsync("/api/v1/admin/tenants", request);

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.Created || response.StatusCode == HttpStatusCode.OK);
        
        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(request.tenantId, result.TenantId);
    }

    [Fact]
    public async Task UpdateTenant_WithValidRequest_UpdatesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var tenant = await CreateTestTenantAsync(tenantId);
        
        var request = new
        {
            name = "Updated Tenant Name",
            description = "Updated description"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<Tenant>(response);
        Assert.NotNull(result);
        Assert.Equal(request.name, result.Name);
    }

    [Fact]
    public async Task DeleteTenant_WithValidId_DeletesTenant()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var tenant = await CreateTestTenantAsync(tenantId);

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}");

        // Assert
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.NoContent);
        
        // Verify deletion
        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteTenant_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        
        var invalidTenantId = $"non-existent-{Guid.NewGuid()}";

        // Act
        var response = await DeleteAsync($"/api/v1/admin/tenants/{invalidTenantId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}


