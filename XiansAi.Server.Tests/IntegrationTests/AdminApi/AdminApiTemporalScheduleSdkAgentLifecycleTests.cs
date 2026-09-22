using System.Net;
using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Scheduling;
using Xians.Lib.Agents.Scheduling.Models;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System scheduling agent authored with Xians.Lib. Chat ExecuteAsync a Manage workflow
/// that calls ScheduleCollection CreateIfNotExists/Exists/List/Get/Pause/Unpause/Trigger/Delete
/// from a Temporal activity and, in a second test, from workflow code (Lib stubs Temporal
/// RPCs to ScheduleActivities). Tick is the scheduled workflow. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/scheduling/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalScheduleSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalScheduleSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task ScheduleSdkAgent_CollectionOps_FromActivity()
        => RunCollectionCycleAsync(fromWorkflow: false);

    [Fact]
    public Task ScheduleSdkAgent_CollectionOps_FromWorkflow()
        => RunCollectionCycleAsync(fromWorkflow: true);

    private async Task RunCollectionCycleAsync(bool fromWorkflow)
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"SchedSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        const string scheduleName = "managed";
        var scheduleId = $"{ownerTenant}:{agentName}:{activationName}:{scheduleName}";
        var schedulesPath = $"/api/v1/admin/tenants/{ownerTenant}/agents/{Uri.EscapeDataString(agentName)}/schedules";
        var expectedRun = fromWorkflow ? "run:ok:workflow:" : "run:ok:activity:";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterScheduleSdkAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var activationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, expectedRun, userText: "run");
        Assert.True(
            await WaitForScheduleInListAsync(schedulesPath, scheduleId),
            $"Schedule {scheduleId} was not created by ScheduleCollection.");
        Assert.True(
            await WaitForScheduleHistoryCountAsync(schedulesPath, scheduleId, 1),
            $"Schedule {scheduleId} did not record a Trigger action.");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "cleanup:ok", userText: "cleanup");
        var missing = await GetAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterScheduleSdkAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(agentName, "Manages schedules through ScheduleCollection");
        var manage = agent.Workflows.DefineCustom<ScheduleSdkManageWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new ScheduleSdkActivities());
        agent.Workflows.DefineCustom<ScheduleSdkTickWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Tick");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleScheduleSdkChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleScheduleSdkChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        var command = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<ScheduleSdkManageWorkflow, string>(
                new object[] { command, fromWorkflow },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:Manage")]
public class ScheduleSdkManageWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command, bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return ScheduleSdkDispatch.RunAsync(command);
        }

        return Workflow.ExecuteActivityAsync(
            (ScheduleSdkActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(60) });
    }
}

public class ScheduleSdkActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return ScheduleSdkDispatch.RunAsync(command);
    }
}

internal static class ScheduleSdkDispatch
{
    private const string ScheduleName = "managed";
    private const string GhostName = "ghost";

    public static Task<string> RunAsync(string command)
    {
        if (command == "run")
        {
            return RunManageAsync();
        }

        if (command == "cleanup")
        {
            return RunCleanupAsync();
        }

        return Task.FromResult("unknown");
    }

    private static async Task<string> RunManageAsync()
    {
        var schedules = XiansContext.CurrentAgent.Schedules;
        var context = Workflow.InWorkflow ? "workflow" : "activity";

        if (await schedules.ExistsAsync(GhostName))
        {
            return $"run:ghost-exists:{context}";
        }

        await CreateManagedScheduleAsync(schedules);
        await CreateManagedScheduleAsync(schedules);

        if (!await schedules.ExistsAsync(ScheduleName))
        {
            return $"run:missing-after-create:{context}";
        }

        var listed = await schedules.ListAsync();
        var created = listed.FirstOrDefault(item =>
            item.Id.EndsWith($":{ScheduleName}", StringComparison.Ordinal));
        if (created == null)
        {
            return $"run:not-listed:{context}";
        }

        var fetched = await schedules.GetAsync(ScheduleName);
        if (!string.Equals(fetched.Id, created.Id, StringComparison.Ordinal))
        {
            return $"run:get-mismatch:{context}:{fetched.Id}";
        }

        await schedules.PauseAsync(ScheduleName, "test-pause");
        var paused = await fetched.GetSnapshotAsync();
        if (!paused.Paused)
        {
            return $"run:pause-failed:{context}";
        }

        await schedules.UnpauseAsync(ScheduleName, "test-resume");
        var resumed = await (await schedules.GetAsync(ScheduleName)).GetSnapshotAsync();
        if (resumed.Paused)
        {
            return $"run:unpause-failed:{context}";
        }

        await schedules.PauseAsync(ScheduleName, "hold-for-trigger");
        await schedules.TriggerAsync(ScheduleName);
        return $"run:ok:{context}:{created.Id}";
    }

    private static async Task<string> RunCleanupAsync()
    {
        var schedules = XiansContext.CurrentAgent.Schedules;
        await schedules.DeleteAsync(ScheduleName);
        if (await schedules.ExistsAsync(ScheduleName))
        {
            return "cleanup:still-exists";
        }

        try
        {
            await schedules.GetAsync(ScheduleName);
            return "cleanup:get-still-there";
        }
        catch (Exception ex) when (IsScheduleNotFound(ex))
        {
            return "cleanup:ok";
        }
    }

    private static Task CreateManagedScheduleAsync(ScheduleCollection schedules)
    {
        return schedules
            .Create<ScheduleSdkTickWorkflow>(ScheduleName)
            .EverySeconds(60)
            .SkipIfRunning()
            .CreateIfNotExistsAsync();
    }

    private static bool IsScheduleNotFound(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is ScheduleNotFoundException)
            {
                return true;
            }

            if (current.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

[Workflow("placeholder:SdkTick")]
public class ScheduleSdkTickWorkflow
{
    [WorkflowRun]
    public Task RunAsync()
    {
        return Task.CompletedTask;
    }
}
