using System.Net;
using System.Text.Json;
using Features.WebApi.Models;
using Temporalio.Common;
using Temporalio.Worker;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Admin API HTTP tests for Temporal schedules, HITL tasks, and worker deployments.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalScheduleAndTaskTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalScheduleAndTaskTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task Schedule_CreatePauseResumeAndDelete_RoundTrips()
    {
        var (tenantId, agent, flow) = await SeedTenantAgentAndFlowAsync();
        var activationName = "front-desk";
        var scheduleId = $"{tenantId}:{agent.Name}:sched-{Guid.NewGuid():N}";
        await CreateAgentScheduleAsync(tenantId, agent.Name, flow.WorkflowType, scheduleId, activationName);

        var encodedAgent = Uri.EscapeDataString(agent.Name);
        var basePath = $"/api/v1/admin/tenants/{tenantId}/agents/{encodedAgent}/schedules";

        var list = await GetAsync(basePath);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listJson.RootElement.EnumerateArray(), item =>
            string.Equals(item.GetProperty("id").GetString(), scheduleId, StringComparison.Ordinal));

        var byId = await GetAsync($"{basePath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);

        var upcoming = await GetAsync($"{basePath}/upcoming-runs?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, upcoming.StatusCode);

        var pause = await PostAsJsonAsync($"{basePath}/pause?scheduleId={Uri.EscapeDataString(scheduleId)}", new { });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);

        var resume = await PostAsJsonAsync($"{basePath}/resume?scheduleId={Uri.EscapeDataString(scheduleId)}", new { });
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);

        var deleteById = await DeleteAsync($"{basePath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, deleteById.StatusCode);

        var leftoverId = $"{tenantId}:{agent.Name}:sched-{Guid.NewGuid():N}";
        await CreateAgentScheduleAsync(tenantId, agent.Name, flow.WorkflowType, leftoverId, activationName);
        var deleteByActivation = await DeleteAsync($"{basePath}/activation/{Uri.EscapeDataString(activationName)}");
        Assert.Equal(HttpStatusCode.OK, deleteByActivation.StatusCode);
        using var deletedJson = JsonDocument.Parse(await deleteByActivation.Content.ReadAsStringAsync());
        Assert.True(deletedJson.RootElement.GetProperty("deletedCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task HitlTask_GetDraftActionAndStats_RoundTrip()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();
        var activationName = "front-desk";
        var taskQueue = $"hitl_task:{Guid.NewGuid():N}";
        await using var worker = await StartWorkerAsync(taskQueue);
        var workflowId = await StartHitlTaskAsync(tenantId, agent.Name, activationName, taskQueue);

        var listed = await WaitForHitlTaskAsync(tenantId, workflowId);
        Assert.True(listed, $"HITL task {workflowId} did not appear in Temporal visibility.");

        var get = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/tasks/by-id?taskId={Uri.EscapeDataString(workflowId)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var getJson = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(workflowId, getJson.RootElement.GetProperty("workflowId").GetString());

        var draft = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/tasks/draft?taskId={Uri.EscapeDataString(workflowId)}",
            new UpdateDraftRequest { UpdatedDraft = "revised draft" });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);

        var metadata = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/tasks/metadata?taskId={Uri.EscapeDataString(workflowId)}",
            new UpdateMetadataRequest { Metadata = new Dictionary<string, object> { ["source"] = "tests" } });
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);

        var action = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/tasks/actions?taskId={Uri.EscapeDataString(workflowId)}",
            new PerformActionRequest { Action = "approve", Comment = "looks good" });
        Assert.Equal(HttpStatusCode.OK, action.StatusCode);

        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow.AddHours(1);
        var stats = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/stats?startDate={Uri.EscapeDataString(start.ToString("o"))}&endDate={Uri.EscapeDataString(end.ToString("o"))}");
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        using var statsJson = JsonDocument.Parse(await stats.Content.ReadAsStringAsync());
        Assert.True(statsJson.RootElement.GetProperty("tasks").GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task WorkerDeployment_ListDescribeAndPromote_Succeeds()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var deploymentName = $"deploy-{Guid.NewGuid():N}";
        var buildId = "1.0.0";
        var taskQueue = $"deploy-q-{Guid.NewGuid():N}";
        var deploymentOptions = new WorkerDeploymentOptions
        {
            Version = new WorkerDeploymentVersion(deploymentName, buildId),
            UseWorkerVersioning = true,
            DefaultVersioningBehavior = VersioningBehavior.AutoUpgrade
        };

        await using var worker = await StartWorkerAsync(taskQueue, deploymentOptions);
        await Task.Delay(1000);

        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/worker-deployments");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listJson.RootElement.EnumerateArray(), item =>
            string.Equals(item.GetProperty("name").GetString(), deploymentName, StringComparison.Ordinal));

        var describe = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/worker-deployments/{Uri.EscapeDataString(deploymentName)}");
        Assert.Equal(HttpStatusCode.OK, describe.StatusCode);

        var promote = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/worker-deployments/{Uri.EscapeDataString(deploymentName)}/set-current-version",
            new { buildId });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
    }

    private async Task<bool> WaitForHitlTaskAsync(string tenantId, string workflowId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/tasks");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in json.RootElement.GetProperty("tasks").EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("workflowId").GetString(), workflowId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            await Task.Delay(250);
        }

        return false;
    }
}
