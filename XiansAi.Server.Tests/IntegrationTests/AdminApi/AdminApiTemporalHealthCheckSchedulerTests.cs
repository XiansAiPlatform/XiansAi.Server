using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data.Models.HealthChecks;
using Shared.Services.HealthChecks;
using Temporalio.Api.Enums.V1;
using Temporalio.Client.Schedules;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// The real <see cref="HealthCheckScheduler"/> against the local Temporal dev server: schedules are
/// created in the platform namespace with the check's interval, overlap=Skip and paused state, kept in
/// line on update, listed by prefix, and deleted idempotently.
/// </summary>
public class AdminApiTemporalHealthCheckSchedulerTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalHealthCheckSchedulerTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    private IHealthCheckScheduler Scheduler => _factory.Services.GetRequiredService<IHealthCheckScheduler>();

    private static AgentHealthCheck Check(string tenantId, int intervalMinutes = 15, bool enabled = true) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        TenantId = tenantId,
        ActivationId = ObjectId.GenerateNewId().ToString(),
        AgentName = "Support Agent",
        ActivationName = "prod",
        Name = "greets",
        Type = HealthCheckType.Chat,
        Enabled = enabled,
        IntervalMinutes = intervalMinutes,
        TimeoutSeconds = 30,
        CreatedBy = "admin",
        Chat = new ChatHealthCheckConfig { Message = "Hello" }
    };

    private Task<ScheduleDescription> DescribeAsync(AgentHealthCheck check) =>
        Temporal.Environment.Client
            .GetScheduleHandle(HealthCheckScheduler.GetScheduleId(check.TenantId, check.Id))
            .DescribeAsync();

    [Fact]
    public async Task Upsert_CreatesThenUpdatesThePlatformSchedule()
    {
        var check = Check($"tenant-{Guid.NewGuid():N}");

        var created = await Scheduler.UpsertAsync(check, activationActive: true);

        Assert.Equal(HealthCheckScheduleState.Active, created);
        var description = await DescribeAsync(check);
        Assert.False(description.Schedule.State.Paused);
        Assert.Equal(ScheduleOverlapPolicy.Skip, description.Schedule.Policy.Overlap);
        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(description.Schedule.Spec.Intervals).Every);
        var action = Assert.IsType<ScheduleActionStartWorkflow>(description.Schedule.Action);
        Assert.Equal(AgentHealthCheckWorkflow.Name, action.Workflow);

        check.IntervalMinutes = 30;
        var updated = await Scheduler.UpsertAsync(check, activationActive: false);

        Assert.Equal(HealthCheckScheduleState.Paused, updated);
        description = await DescribeAsync(check);
        Assert.True(description.Schedule.State.Paused);
        Assert.Equal("activation deactivated", description.Schedule.State.Note);
        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(description.Schedule.Spec.Intervals).Every);

        await Scheduler.DeleteAsync(check.TenantId, check.Id);
    }

    [Fact]
    public async Task DisabledCheck_IsCreatedPaused()
    {
        var check = Check($"tenant-{Guid.NewGuid():N}", enabled: false);

        var state = await Scheduler.UpsertAsync(check, activationActive: true);

        Assert.Equal(HealthCheckScheduleState.Paused, state);
        Assert.True((await DescribeAsync(check)).Schedule.State.Paused);

        await Scheduler.DeleteAsync(check.TenantId, check.Id);
    }

    /// <summary>
    /// End to end through the server's own worker: a schedule check created over the API is
    /// triggered in Temporal, the health-check workflow runs the activity in-process, and the
    /// result lands in the run history and the check's status.
    /// </summary>
    [Fact]
    public async Task TriggeredScheduleCheck_IsExecutedByTheServerWorkerAndRecorded()
    {
        var (tenantId, agent, flow, activation) = await SeedActiveActivationAsync();
        var agentScheduleId = $"{tenantId}:{agent.Name}:nightly-{Guid.NewGuid():N}";
        await CreateAgentScheduleAsync(tenantId, agent.Name, flow.WorkflowType, agentScheduleId, activation.Name);

        var create = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/health-checks",
            new
            {
                name = "nightly schedule alive",
                type = "schedule",
                intervalMinutes = 60,
                schedule = new { scheduleId = agentScheduleId, requireLastRunSuccess = true }
            });
        Assert.Equal(System.Net.HttpStatusCode.Created, create.StatusCode);
        var check = (await ReadAsJsonAsync<AgentHealthCheck>(create))!;
        Assert.Equal(HealthCheckScheduleState.Active, check.ScheduleState);

        await Temporal.Environment.Client
            .GetScheduleHandle(HealthCheckScheduler.GetScheduleId(tenantId, check.Id))
            .TriggerAsync();

        AgentHealthCheckRun? run = null;
        for (var attempt = 0; attempt < 60 && run == null; attempt++)
        {
            using var scope = _factory.Services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<Shared.Repositories.IAgentHealthCheckRepository>();
            run = (await repository.ListRunsAsync(tenantId, check.Id, null, null, null, 1, 1)).Items.FirstOrDefault();
            if (run == null) await Task.Delay(500);
        }

        Assert.NotNull(run);
        Assert.True(run!.Status == HealthCheckStatus.Pass, $"Expected pass but got {run.Status}: {run.Detail}");
        Assert.False(string.IsNullOrEmpty(run.WorkflowRunId));

        var stored = (await ReadAsJsonAsync<AgentHealthCheck>(await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/health-checks/{check.Id}")))!;
        Assert.Equal(HealthCheckStatus.Pass, stored.LastStatus);
        Assert.Equal(0, stored.ConsecutiveFailures);

        await Scheduler.DeleteAsync(tenantId, check.Id);
    }

    /// <summary>
    /// The webhook eval form's dropdowns: a running workflow reports its update methods through
    /// Temporal's metadata query; a registered workflow that isn't running is listed without methods.
    /// </summary>
    [Fact]
    public async Task WebhookTargets_ListUpdateMethodsOfRunningWorkflows()
    {
        var (tenantId, agent, chatFlow, activation) = await SeedActiveActivationAsync();
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId, flowName: "Git Events");

        // A builtin webhook registered for this activation, created the way Connections -> Webhooks does.
        var webhook = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/webhooks", new
        {
            activationName = activation.Name,
            agentName = agent.Name,
            workflowName = "Git Events",
            webhookName = "github-push"
        });
        Assert.True(webhook.IsSuccessStatusCode, await webhook.Content.ReadAsStringAsync());

        var workflowType = $"{agent.Name}:Git Events";
        var taskQueue = $"{tenantId}:{workflowType}";
        using var cts = new CancellationTokenSource();
        using var worker = new Temporalio.Worker.TemporalWorker(
            Temporal.Environment.Client,
            new Temporalio.Worker.TemporalWorkerOptions(taskQueue).AddWorkflow<WebhookTargetStubWorkflow>());
        var run = worker.ExecuteAsync(cts.Token);

        try
        {
            await Temporal.Environment.Client.StartWorkflowAsync(
                workflowType,
                Array.Empty<object?>(),
                new Temporalio.Client.WorkflowOptions(
                    global::WorkflowIdentifier.BuildWorkflowId(tenantId, agent.Name, "Git Events", activation.Name),
                    taskQueue));

            var response = await GetAsync(
                $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activation.Id}/health-checks/webhook-targets");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var targets = json.RootElement.EnumerateArray().ToDictionary(t => t.GetProperty("workflowType").GetString()!);

            var gitEvents = targets["Git Events"];
            Assert.Equal("running", gitEvents.GetProperty("status").GetString());
            Assert.Equal(
                ["github-push", "health-ping"],
                gitEvents.GetProperty("methods").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToArray());
            Assert.True(gitEvents.GetProperty("isBuiltIn").GetBoolean());
            Assert.Equal(["github-push"], gitEvents.GetProperty("webhooks").EnumerateArray().Select(w => w.GetString()).ToArray());

            var chat = targets[ShortFlow(chatFlow.WorkflowType)];
            Assert.Equal("notRunning", chat.GetProperty("status").GetString());
            Assert.Empty(chat.GetProperty("methods").EnumerateArray());
        }
        finally
        {
            cts.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    private static string ShortFlow(string workflowType) => workflowType[(workflowType.IndexOf(':') + 1)..];

    [Fact]
    public async Task List_ReturnsOnlyHealthCheckSchedules_AndDeleteIsIdempotent()
    {
        var check = Check($"tenant-{Guid.NewGuid():N}");
        await Scheduler.UpsertAsync(check, activationActive: true);
        var scheduleId = HealthCheckScheduler.GetScheduleId(check.TenantId, check.Id);

        // Temporal's schedule listing is eventually consistent; give visibility a moment.
        List<string> ids = [];
        for (var attempt = 0; attempt < 20 && !ids.Contains(scheduleId); attempt++)
        {
            ids = await Scheduler.ListScheduleIdsAsync();
            if (!ids.Contains(scheduleId)) await Task.Delay(250);
        }

        Assert.Contains(scheduleId, ids);
        Assert.All(ids, id => Assert.StartsWith(HealthCheckScheduler.ScheduleIdPrefix, id));

        await Scheduler.DeleteAsync(check.TenantId, check.Id);
        await Scheduler.DeleteAsync(check.TenantId, check.Id); // already gone: no exception
    }
}
