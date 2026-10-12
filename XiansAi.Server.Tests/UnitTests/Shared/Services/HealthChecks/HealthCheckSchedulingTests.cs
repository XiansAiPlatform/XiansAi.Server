using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Services.HealthChecks;
using Tests.TestUtils;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

/// <summary>Activation health summary, schedule ids, and the schedule reconciler.</summary>
public class HealthCheckSchedulingTests
{
    private static AgentActivation Activation(string id = "66f0000000000000000000a1", bool active = true, string tenantId = "acme") => new()
    {
        Id = id,
        Name = "prod",
        AgentName = "Support Agent",
        TenantId = tenantId,
        CreatedBy = "admin",
        Active = active
    };

    private static AgentHealthCheck Check(
        string id,
        HealthCheckStatus? lastStatus = null,
        bool enabled = true,
        string activationId = "66f0000000000000000000a1",
        HealthCheckScheduleState scheduleState = HealthCheckScheduleState.Active) => new()
    {
        Id = id,
        TenantId = "acme",
        ActivationId = activationId,
        AgentName = "Support Agent",
        ActivationName = "prod",
        Name = $"check-{id}",
        Type = HealthCheckType.Chat,
        Enabled = enabled,
        IntervalMinutes = 5,
        TimeoutSeconds = 30,
        LastStatus = lastStatus,
        LastRunAt = lastStatus == null ? null : DateTime.UtcNow,
        ScheduleState = scheduleState,
        CreatedBy = "admin"
    };

    // ----- Summary -----

    [Fact]
    public void Summary_NoRunsYet_IsUnknown()
    {
        var summary = AgentHealthCheckService.BuildSummary(Activation(), [Check("1"), Check("2")]);

        Assert.Equal(ActivationHealthStatus.Unknown, summary.Status);
        Assert.Null(summary.LastRunAt);
    }

    [Fact]
    public void Summary_AllPassing_IsHealthy()
    {
        var summary = AgentHealthCheckService.BuildSummary(Activation(),
            [Check("1", HealthCheckStatus.Pass), Check("2", HealthCheckStatus.Pass), Check("3")]);

        Assert.Equal(ActivationHealthStatus.Healthy, summary.Status);
        Assert.Equal(2, summary.Passing);
        Assert.Equal(3, summary.TotalChecks);
    }

    [Fact]
    public void Summary_Mixed_IsDegraded()
    {
        var summary = AgentHealthCheckService.BuildSummary(Activation(),
            [Check("1", HealthCheckStatus.Pass), Check("2", HealthCheckStatus.Error)]);

        Assert.Equal(ActivationHealthStatus.Degraded, summary.Status);
        Assert.Equal(1, summary.Erroring);
    }

    [Fact]
    public void Summary_NonePassing_IsUnhealthy()
    {
        var summary = AgentHealthCheckService.BuildSummary(Activation(),
            [Check("1", HealthCheckStatus.Fail), Check("2", HealthCheckStatus.Error)]);

        Assert.Equal(ActivationHealthStatus.Unhealthy, summary.Status);
    }

    [Fact]
    public void Summary_IgnoresDisabledChecks()
    {
        var summary = AgentHealthCheckService.BuildSummary(Activation(),
            [Check("1", HealthCheckStatus.Pass), Check("2", HealthCheckStatus.Fail, enabled: false)]);

        Assert.Equal(ActivationHealthStatus.Healthy, summary.Status);
        Assert.Equal(1, summary.EnabledChecks);
        Assert.Equal(2, summary.Checks.Count);
    }

    // ----- Schedule ids and jitter -----

    [Fact]
    public void ScheduleId_RoundTripsTenantIdsContainingColons()
    {
        var id = HealthCheckScheduler.GetScheduleId("org:acme", "66f000000000000000000001");

        Assert.Equal("healthcheck:org:acme:66f000000000000000000001", id);
        Assert.Equal(("org:acme", "66f000000000000000000001"), HealthCheckReconciler.ParseScheduleId(id));
    }

    [Theory]
    [InlineData("healthcheck:")]
    [InlineData("healthcheck:acme")]
    [InlineData("healthcheck:acme:")]
    [InlineData("acme:Support Agent:nightly")]
    public void ParseScheduleId_RejectsForeignOrMalformedIds(string scheduleId)
    {
        Assert.Equal((null, null), HealthCheckReconciler.ParseScheduleId(scheduleId));
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(10, 60)]
    [InlineData(1440, 60)]
    public void Jitter_IsATenthOfTheIntervalCappedAtOneMinute(int intervalMinutes, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), HealthCheckScheduler.GetJitter(intervalMinutes));
    }

    [Theory]
    [InlineData("Percy V3:Process Orchestration Workflow", "Process Orchestration Workflow")]
    [InlineData("GitHub Webhook Agent:Git Events Workflow", "Git Events Workflow")]
    [InlineData("NoAgentPrefix", "NoAgentPrefix")]
    public void WebhookTargets_StripTheAgentPrefixFromWorkflowTypes(string workflowType, string flow)
    {
        Assert.Equal(flow, WebhookTargetDiscovery.ToFlowName(workflowType));
    }

    // ----- Reconciler -----

    private sealed class ReconcilerHarness
    {
        public Mock<IAgentHealthCheckRepository> Repository { get; } = new();
        public Mock<IActivationRepository> Activations { get; } = new();
        public FakeHealthCheckScheduler Scheduler { get; } = new();

        public HealthCheckReconciler Build(params AgentHealthCheck[] checks)
        {
            Repository.Setup(r => r.ListAllAsync()).ReturnsAsync(checks.ToList());
            return new HealthCheckReconciler(Repository.Object, Activations.Object, Scheduler, NullLogger<HealthCheckReconciler>.Instance);
        }
    }

    [Fact]
    public async Task Reconcile_CreatesMissingAndPendingSchedules()
    {
        var harness = new ReconcilerHarness();
        harness.Activations.Setup(a => a.GetByIdAsync("66f0000000000000000000a1")).ReturnsAsync(Activation());
        var pending = Check("66f000000000000000000001", scheduleState: HealthCheckScheduleState.Pending);
        var missing = Check("66f000000000000000000002", scheduleState: HealthCheckScheduleState.Active);

        var result = await harness.Build(pending, missing).ReconcileAsync(CancellationToken.None);

        Assert.Equal(2, result.Synced);
        Assert.False(harness.Scheduler.Get("acme", pending.Id)!.Paused);
        Assert.NotNull(harness.Scheduler.Get("acme", missing.Id));
        harness.Repository.Verify(r => r.SetScheduleStateAsync(pending.Id, HealthCheckScheduleState.Active), Times.Once);
    }

    [Fact]
    public async Task Reconcile_PausesChecksOfInactiveActivations()
    {
        var harness = new ReconcilerHarness();
        harness.Activations.Setup(a => a.GetByIdAsync("66f0000000000000000000a1")).ReturnsAsync(Activation(active: false));
        var check = Check("66f000000000000000000001", scheduleState: HealthCheckScheduleState.Active);
        harness.Scheduler.AddOrphan("acme", check.Id); // exists, but running

        var result = await harness.Build(check).ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, result.Synced);
        Assert.True(harness.Scheduler.Get("acme", check.Id)!.Paused);
    }

    [Fact]
    public async Task Reconcile_LeavesInSyncSchedulesAlone()
    {
        var harness = new ReconcilerHarness();
        harness.Activations.Setup(a => a.GetByIdAsync("66f0000000000000000000a1")).ReturnsAsync(Activation());
        var check = Check("66f000000000000000000001", scheduleState: HealthCheckScheduleState.Active);
        harness.Scheduler.AddOrphan("acme", check.Id);

        var result = await harness.Build(check).ReconcileAsync(CancellationToken.None);

        Assert.Equal(0, result.Synced);
        harness.Repository.Verify(r => r.SetScheduleStateAsync(It.IsAny<string>(), It.IsAny<HealthCheckScheduleState>()), Times.Never);
    }

    [Fact]
    public async Task Reconcile_DeletesChecksWhoseActivationIsGone_AndTheirSchedules()
    {
        var harness = new ReconcilerHarness();
        harness.Activations.Setup(a => a.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((AgentActivation?)null);
        var orphan = Check("66f000000000000000000001");
        harness.Scheduler.AddOrphan("acme", orphan.Id);

        var result = await harness.Build(orphan).ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, result.OrphanChecksDeleted);
        Assert.Equal(1, result.OrphanSchedulesDeleted);
        Assert.Null(harness.Scheduler.Get("acme", orphan.Id));
        harness.Repository.Verify(r => r.DeleteAsync("acme", orphan.Id), Times.Once);
        harness.Repository.Verify(r => r.DeleteRunsByCheckAsync("acme", orphan.Id), Times.Once);
    }

    [Fact]
    public async Task Reconcile_DeletesSchedulesWithNoCheck()
    {
        var harness = new ReconcilerHarness();
        harness.Scheduler.AddOrphan("acme", "66f0000000000000000000ff");

        var result = await harness.Build().ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, result.OrphanSchedulesDeleted);
        Assert.Empty(harness.Scheduler.Schedules);
    }

    [Fact]
    public async Task Reconcile_CountsFailuresAndContinues()
    {
        var harness = new ReconcilerHarness();
        harness.Activations.Setup(a => a.GetByIdAsync("66f0000000000000000000a1")).ReturnsAsync(Activation());
        harness.Activations.Setup(a => a.GetByIdAsync("66f0000000000000000000a2")).ThrowsAsync(new TimeoutException("mongo"));
        var broken = Check("66f000000000000000000001", activationId: "66f0000000000000000000a2", scheduleState: HealthCheckScheduleState.Pending);
        var healthy = Check("66f000000000000000000002", scheduleState: HealthCheckScheduleState.Pending);

        var result = await harness.Build(broken, healthy).ReconcileAsync(CancellationToken.None);

        Assert.Equal(1, result.Failures);
        Assert.Equal(1, result.Synced);
        Assert.NotNull(harness.Scheduler.Get("acme", healthy.Id));
    }
}
