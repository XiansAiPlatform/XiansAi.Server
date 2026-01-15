using System.Net;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminOwnershipEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminOwnershipEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetOwnership_WithValidAgent_ReturnsOwnershipInfo()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/ownership");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.Contains("ownerAccess", content);
        Assert.Contains("readAccess", content);
        Assert.Contains("writeAccess", content);
    }

    [Fact]
    public async Task GetOwnership_WithInvalidAgent_ReturnsNotFound()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var invalidId = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/agents/{invalidId}/ownership");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_WithValidRequest_TransfersOwnership()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var newAdminUserId = $"new-admin-{Guid.NewGuid()}";
        await CreateTestUserWithRoleAsync(newAdminUserId, tenantId, SystemRoles.TenantAdmin);
        
        var request = new
        {
            newAdminId = newAdminUserId
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/ownership", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
        Assert.Contains(newAdminUserId, content);
    }

    [Fact]
    public async Task TransferOwnership_WithInvalidUser_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        
        var request = new
        {
            newAdminId = "non-existent-user"
        };

        // Act
        var response = await PatchAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agents/{agent.Id}/ownership", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}


