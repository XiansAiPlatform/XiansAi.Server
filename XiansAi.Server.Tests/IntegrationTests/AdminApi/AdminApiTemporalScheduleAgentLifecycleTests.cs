using System.Net;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Scheduling;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System scheduling agent authored with Xians.Lib. An Activable Setup workflow creates an
/// interval schedule that starts Tick. Admin list/get/history/pause/resume/delete round-trip
/// that schedule. Schedule IDs follow {tenant}:{agent}:{activation}:{scheduleName}.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalScheduleAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalScheduleAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task SchedulerAgent_ActivableSetup_CreatesScheduleAndAdminOps()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Scheduler {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        const string scheduleName = "tick";
        var setupId = $"{ownerTenant}:{agentName}:Setup:{activationName}";
        var scheduleId = $"{ownerTenant}:{agentName}:{activationName}:{scheduleName}";
        var schedulesPath = $"/api/v1/admin/tenants/{ownerTenant}/agents/{Uri.EscapeDataString(agentName)}/schedules";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterSchedulerAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var (activationId, startedIds) = await ActivateLibAgentWithWorkflowsAsync(
            ownerTenant, agentName, activationName);
        Assert.Contains(setupId, startedIds);

        BindTenantContext(ownerTenant, _adminUserId!);
        Assert.True(
            await WaitForScheduleInListAsync(schedulesPath, scheduleId),
            $"Schedule {scheduleId} was not created by Setup.");

        var listed = await GetAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using (var listedJson = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
        {
            Assert.Equal(scheduleId, listedJson.RootElement.GetProperty("id").GetString());
            Assert.Equal($"{agentName}:Tick", listedJson.RootElement.GetProperty("workflowType").GetString());
        }

        var upcoming = await GetAsync($"{schedulesPath}/upcoming-runs?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, upcoming.StatusCode);

        Assert.True(
            await WaitForScheduleHistoryCountAsync(schedulesPath, scheduleId, 2),
            $"Schedule {scheduleId} did not fire Tick at least twice.");

        var pause = await PostAsJsonAsync(
            $"{schedulesPath}/pause?scheduleId={Uri.EscapeDataString(scheduleId)}&note=test-pause",
            new { });
        Assert.Equal(HttpStatusCode.OK, pause.StatusCode);
        await AssertScheduleStatusAsync(schedulesPath, scheduleId, "Suspended");

        var pausedCount = await ReadExecutionCountAsync(schedulesPath, scheduleId);
        await Task.Delay(2500);
        Assert.Equal(pausedCount, await ReadExecutionCountAsync(schedulesPath, scheduleId));

        var resume = await PostAsJsonAsync(
            $"{schedulesPath}/resume?scheduleId={Uri.EscapeDataString(scheduleId)}",
            new { });
        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        await AssertScheduleStatusAsync(schedulesPath, scheduleId, "Running");

        BindTenantContext(otherTenant, _adminUserId!);
        var otherList = await GetAsync(
            $"/api/v1/admin/tenants/{otherTenant}/agents/{Uri.EscapeDataString(agentName)}/schedules");
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        using (var otherJson = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync()))
        {
            Assert.DoesNotContain(otherJson.RootElement.EnumerateArray(), item =>
                string.Equals(item.GetProperty("id").GetString(), scheduleId, StringComparison.Ordinal));
        }

        BindTenantContext(ownerTenant, _adminUserId!);
        var deleteById = await DeleteAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, deleteById.StatusCode);
        var missing = await GetAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterSchedulerAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Creates a Tick schedule from an activable Setup workflow");
        agent.Workflows.DefineCustom<ScheduleSetupWorkflow>(
            new WorkflowOptions { Activable = true },
            typeName: $"{agentName}:Setup");
        agent.Workflows.DefineCustom<ScheduleTickWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Tick");
        return agent;
    }

    private async Task<bool> WaitForScheduleInListAsync(string schedulesPath, string scheduleId)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(schedulesPath);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("id").GetString(), scheduleId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            await Task.Delay(250);
        }

        return false;
    }

    private async Task<bool> WaitForScheduleHistoryCountAsync(string schedulesPath, string scheduleId, int minimum)
    {
        var uri = $"{schedulesPath}/history?scheduleId={Uri.EscapeDataString(scheduleId)}";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.GetArrayLength() >= minimum)
                {
                    return true;
                }
            }

            await Task.Delay(500);
        }

        return false;
    }

    private async Task AssertScheduleStatusAsync(string schedulesPath, string scheduleId, string expectedStatus)
    {
        var uri = $"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}";
        var lastBody = string.Empty;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                var status = json.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Schedule {scheduleId} did not reach status {expectedStatus}. Last: {lastBody}");
    }

    private async Task<long> ReadExecutionCountAsync(string schedulesPath, string scheduleId)
    {
        var response = await GetAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("executionCount").GetInt64();
    }
}

[Workflow("placeholder:Setup")]
public class ScheduleSetupWorkflow
{
    [WorkflowRun]
    public async Task RunAsync()
    {
        await XiansContext.CurrentAgent.Schedules
            .Create<ScheduleTickWorkflow>("tick")
            .EverySeconds(1)
            .SkipIfRunning()
            .CreateIfNotExistsAsync();
    }
}

[Workflow("placeholder:Tick")]
public class ScheduleTickWorkflow
{
    [WorkflowRun]
    public Task RunAsync()
    {
        return Task.CompletedTask;
    }
}
