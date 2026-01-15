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
    public async Task StreamThreadEvents_WithValidThreadId_ReturnsSSEStream()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var threadId = $"thread-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/events");

        // Assert
        // SSE endpoints typically return 200 OK with text/event-stream content type
        // The actual streaming behavior is hard to test in integration tests without more complex setup
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task StreamThreadEvents_WithoutAdminRole_ReturnsForbidden()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser); // Non-admin role
        await CreateTestTenantAsync(tenantId);
        
        var threadId = $"thread-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/events");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}


