using System.Net;
using System.Text.Json;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Admin API HTTP tests against a real local Temporal CLI/dev server.
/// These do not start a worker: they cover list/get/cancel, schedules,
/// connectivity, empty stats/tasks, and worker-deployment listing.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalEndpointsTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalEndpointsTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task TestTemporalConnection_AgainstLocalServer_ReturnsOk()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/temporal-config/test-connection",
            new UpsertTenantTemporalConfigRequest
            {
                TenantId = tenantId,
                ServerUrl = Temporal.TargetHost,
                Namespace = Temporal.Namespace
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListWorkflows_WithAgentAndNoRuns_ReturnsEmptyPage()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows/list?agent={Uri.EscapeDataString(agent.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("workflows").GetArrayLength());
    }

    [Fact]
    public async Task GetWorkflowTypes_WithAgentAndNoRuns_ReturnsEmptyList()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows/types?agent={Uri.EscapeDataString(agent.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task GetWorkflow_WhenMissingOnTemporal_ReturnsBadRequest()
    {
        var (tenantId, _, flow) = await SeedTenantAgentAndFlowAsync();
        var workflowId = $"{tenantId}:{flow.WorkflowType}:missing-{Guid.NewGuid():N}";

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListSchedules_WithNoSchedules_ReturnsEmptyList()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{Uri.EscapeDataString(agent.Name)}/schedules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task DeleteSchedulesForAgent_WithNone_ReturnsOk()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();

        var response = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/agents/{Uri.EscapeDataString(agent.Name)}/schedules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("deletedCount").GetInt32());
    }

    [Fact]
    public async Task ListTasks_WithNoHitlWorkflows_ReturnsEmptyPage()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public async Task GetStats_WithDateRange_ReturnsZeroTaskCounts()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/stats?startDate={Uri.EscapeDataString(start.ToString("o"))}&endDate={Uri.EscapeDataString(end.ToString("o"))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tasks = json.RootElement.GetProperty("tasks");
        Assert.Equal(0, tasks.GetProperty("total").GetInt32());
        Assert.Equal(0, tasks.GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task ListWorkerDeployments_AsSysAdmin_ReturnsOk()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/worker-deployments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task DescribeWorkerDeployment_WhenMissing_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/worker-deployments/{Uri.EscapeDataString($"missing-{Guid.NewGuid():N}")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivateGetAndCancel_WithoutWorker_Succeeds()
    {
        var (tenantId, agent, flow) = await SeedTenantAgentAndFlowAsync();
        var postfix = $"run-{Guid.NewGuid():N}";

        var activate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows/activate",
            new
            {
                workflowType = flow.WorkflowType,
                agentName = agent.Name,
                workflowIdPostfix = postfix
            });

        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        using var activateJson = JsonDocument.Parse(await activate.Content.ReadAsStringAsync());
        var workflowId = activateJson.RootElement.GetProperty("workflowId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(workflowId));

        var get = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId!)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var getJson = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(workflowId, getJson.RootElement.GetProperty("workflowId").GetString());

        var listed = await WaitForWorkflowInListAsync(tenantId, agent.Name, workflowId!);
        Assert.True(listed, $"Workflow {workflowId} did not appear in Temporal visibility.");

        var events = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows/events?workflowId={Uri.EscapeDataString(workflowId!)}");
        Assert.Equal(HttpStatusCode.OK, events.StatusCode);

        var cancel = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows/cancel?workflowId={Uri.EscapeDataString(workflowId!)}&force=true",
            new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
    }
}
