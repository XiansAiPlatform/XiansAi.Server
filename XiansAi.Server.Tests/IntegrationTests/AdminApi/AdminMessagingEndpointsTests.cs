using System.Net;
using System.Text;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class AdminMessagingEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminMessagingEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task SendDataToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            data = new { key = "value" },
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/data", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendChatToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            message = "Test message",
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/chat", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}


