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
/// Strict ScheduleCollection.CreateAsync (throws if it exists) and activity-only
/// XiansSchedule.DescribeAsync. CreateIfNotExists / GetSnapshot stay on Schedule SDK.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/scheduling/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalScheduleCreateAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalScheduleCreateAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task ScheduleCreateAgent_StrictCreate_DescribeFromActivity()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"SchedCreate {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        const string scheduleName = "strict";
        var scheduleId = $"{tenantId}:{agentName}:{activationName}:{scheduleName}";
        var schedulesPath = $"/api/v1/admin/tenants/{tenantId}/agents/{Uri.EscapeDataString(agentName)}/schedules";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterScheduleCreateAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "run:ok:", userText: "run");
        Assert.True(
            await WaitForScheduleInListAsync(schedulesPath, scheduleId),
            $"Schedule {scheduleId} was not created by CreateAsync.");

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "cleanup:ok", userText: "cleanup");
        var missing = await GetAsync($"{schedulesPath}/by-id?scheduleId={Uri.EscapeDataString(scheduleId)}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterScheduleCreateAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Strict CreateAsync and DescribeAsync from an activity");
        var manage = agent.Workflows.DefineCustom<ScheduleCreateManageWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new ScheduleCreateActivities());
        agent.Workflows.DefineCustom<ScheduleCreateTickWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Tick");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleScheduleCreateChatAsync);
        return agent;
    }

    private static async Task HandleScheduleCreateChatAsync(UserMessageContext context)
    {
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<ScheduleCreateManageWorkflow, string>(
                new object[] { context.Message.Text?.Trim() ?? string.Empty },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:CreateManage")]
public class ScheduleCreateManageWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command)
    {
        return Workflow.ExecuteActivityAsync(
            (ScheduleCreateActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(60) });
    }
}

public class ScheduleCreateActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return ScheduleCreateDispatch.RunAsync(command);
    }
}

internal static class ScheduleCreateDispatch
{
    private const string ScheduleName = "strict";

    public static Task<string> RunAsync(string command)
    {
        if (command == "run")
        {
            return RunCreateAsync();
        }

        if (command == "cleanup")
        {
            return RunCleanupAsync();
        }

        return Task.FromResult("unknown");
    }

    private static async Task<string> RunCreateAsync()
    {
        var schedules = XiansContext.CurrentAgent.Schedules;
        var created = await CreateStrictAsync(schedules);

        try
        {
            await CreateStrictAsync(schedules);
            return "run:duplicate-ok";
        }
        catch (Exception ex) when (ex is ScheduleAlreadyExistsException || HasAlreadyExists(ex))
        {
        }

        var described = await created.DescribeAsync();
        if (!string.Equals(described.Id, created.Id, StringComparison.Ordinal))
        {
            return $"run:id-mismatch:{described.Id}";
        }

        if (!described.Schedule.State.Paused)
        {
            return "run:not-paused";
        }

        return $"run:ok:{created.Id}";
    }

    private static async Task<string> RunCleanupAsync()
    {
        var schedules = XiansContext.CurrentAgent.Schedules;
        await schedules.DeleteAsync(ScheduleName);
        return await schedules.ExistsAsync(ScheduleName) ? "cleanup:still-exists" : "cleanup:ok";
    }

    private static Task<XiansSchedule> CreateStrictAsync(ScheduleCollection schedules)
    {
        return schedules
            .Create<ScheduleCreateTickWorkflow>(ScheduleName)
            .EverySeconds(60)
            .SkipIfRunning()
            .StartPaused(true, "strict-create")
            .CreateAsync();
    }

    private static bool HasAlreadyExists(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

[Workflow("placeholder:CreateTick")]
public class ScheduleCreateTickWorkflow
{
    [WorkflowRun]
    public Task RunAsync()
    {
        return Task.CompletedTask;
    }
}
