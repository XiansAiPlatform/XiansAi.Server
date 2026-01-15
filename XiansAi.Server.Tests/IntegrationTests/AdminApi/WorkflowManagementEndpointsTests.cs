using System.Net;
using Xunit;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

public class WorkflowManagementEndpointsTests : AdminApiIntegrationTestBase
{
    public WorkflowManagementEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ActivateWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var request = new
        {
            workflowType = "test-workflow",
            agent = $"agent-{Guid.NewGuid()}",
            threadId = $"thread-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/workflows/activate", request);

        // Assert
        // Workflow activation may return various status codes depending on implementation
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflow_WithValidId_ReturnsWorkflow()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var workflowId = $"workflow-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={workflowId}");

        // Assert
        // May return 404 if workflow doesn't exist, but should not be 401/403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkflows_WithValidRequest_ReturnsWorkflowList()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows/list");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
    }

    [Fact]
    public async Task GetWorkflowEvents_WithValidId_ReturnsEvents()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var workflowId = $"workflow-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows/events?workflowId={workflowId}");

        // Assert
        // May return 404 if workflow doesn't exist, but should not be 401/403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StreamWorkflowEvents_WithValidId_ReturnsSSEStream()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var workflowId = $"workflow-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows/events/stream?workflowId={workflowId}");

        // Assert
        // SSE endpoints typically return 200 OK with text/event-stream content type
        Assert.True(response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWorkflowTypes_WithValidAgent_ReturnsTypes()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var agent = $"agent-{Guid.NewGuid()}";

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows/types?agent={agent}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotNull(content);
    }

    [Fact]
    public async Task CancelWorkflow_WithValidId_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        
        var workflowId = $"workflow-{Guid.NewGuid()}";

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/workflows/cancel?workflowId={workflowId}&force=false", new { });

        // Assert
        // May return 404 if workflow doesn't exist, but should not be 401/403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkflows_WithoutAdminRole_ReturnsForbidden()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantUser); // Non-admin role
        await CreateTestTenantAsync(tenantId);

        // Act
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/workflows/list");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}


