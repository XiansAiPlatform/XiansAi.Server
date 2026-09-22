using System.Net;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminLogsEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminLogsEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListStreamsAndLogsThenDeleteByActivation_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, name: "front-desk");
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agent.Name, "Chat", activation.Name);
        await SeedLogAsync(tenantId, agent.Name, activation.Name, workflowId, "desk log");

        var streams = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/streams?agentName={Uri.EscapeDataString(agent.Name)}");
        Assert.Equal(HttpStatusCode.OK, streams.StatusCode);
        var streamResult = await ReadAsJsonAsync<AdminLogStreamsResponse>(streams);
        Assert.Contains(streamResult!.Streams, stream => stream.WorkflowId == workflowId);

        var logs = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agent.Name)}&workflowId={Uri.EscapeDataString(workflowId)}");
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        var logResult = await ReadAsJsonAsync<AdminLogsResponse>(logs);
        Assert.Contains(logResult!.Logs, log => log.Message.Contains("desk log"));

        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/agents/{Uri.EscapeDataString(agent.Name)}/activation/{Uri.EscapeDataString(activation.Name)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agent.Name)}&workflowId={Uri.EscapeDataString(workflowId)}");
        Assert.Equal(HttpStatusCode.OK, afterDelete.StatusCode);
        var empty = await ReadAsJsonAsync<AdminLogsResponse>(afterDelete);
        Assert.DoesNotContain(empty!.Logs, log => log.WorkflowId == workflowId);
    }

    [Fact]
    public async Task ListLogs_WithInvalidPage_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
