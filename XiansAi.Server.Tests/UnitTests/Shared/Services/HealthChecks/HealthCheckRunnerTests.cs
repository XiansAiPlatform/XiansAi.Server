using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Services.HealthChecks;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

public class HealthCheckRunnerTests
{
    private const string TenantId = "acme";
    private const string CheckId = "66f000000000000000000001";
    private const string ActivationId = "66f000000000000000000002";

    private readonly Mock<IAgentHealthCheckRepository> _repository = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IHealthCheckExecutor> _executor = new();
    private readonly Mock<IHealthCheckNotifier> _notifier = new();

    public HealthCheckRunnerTests()
    {
        _executor.SetupGet(e => e.Type).Returns(HealthCheckType.Chat);
    }

    private HealthCheckRunner Runner() =>
        new(_repository.Object, _activations.Object, [_executor.Object], _notifier.Object, NullLogger<HealthCheckRunner>.Instance);

    private static AgentHealthCheck Check(bool enabled = true, int timeoutSeconds = 10) => new()
    {
        Id = CheckId,
        TenantId = TenantId,
        ActivationId = ActivationId,
        AgentName = "Support Agent",
        ActivationName = "prod",
        Name = "greets",
        Type = HealthCheckType.Chat,
        Enabled = enabled,
        IntervalMinutes = 5,
        TimeoutSeconds = timeoutSeconds,
        CreatedBy = "admin",
        Chat = new ChatHealthCheckConfig { Message = "Hello" }
    };

    private static AgentActivation Activation(bool active = true, string tenantId = TenantId) => new()
    {
        Id = ActivationId,
        Name = "prod",
        AgentName = "Support Agent",
        TenantId = tenantId,
        CreatedBy = "admin",
        Active = active
    };

    private void Given(AgentHealthCheck? check, AgentActivation? activation)
    {
        _repository.Setup(r => r.GetByIdAsync(TenantId, CheckId)).ReturnsAsync(check);
        _activations.Setup(a => a.GetByIdAsync(ActivationId)).ReturnsAsync(activation);
    }

    private void VerifyNothingRecorded()
    {
        _repository.Verify(r => r.InsertRunAsync(It.IsAny<AgentHealthCheckRun>()), Times.Never);
        _repository.Verify(r => r.RecordRunResultAsync(It.IsAny<string>(), It.IsAny<HealthCheckStatus>(), It.IsAny<DateTime>(), It.IsAny<string?>()), Times.Never);
        _executor.Verify(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<AgentHealthCheckRun>()), Times.Never);
    }

    [Fact]
    public async Task InactiveActivation_IsSkippedWithoutRecording()
    {
        Given(Check(), Activation(active: false));

        var outcome = await Runner().RunAsync(TenantId, CheckId, "run-1", CancellationToken.None);

        Assert.Equal(HealthCheckRunOutcome.Skipped, outcome);
        VerifyNothingRecorded();
    }

    [Fact]
    public async Task MissingActivation_IsSkipped()
    {
        Given(Check(), activation: null);

        Assert.Equal(HealthCheckRunOutcome.Skipped, await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None));
        VerifyNothingRecorded();
    }

    [Fact]
    public async Task ActivationOfAnotherTenant_IsSkipped()
    {
        Given(Check(), Activation(tenantId: "other"));

        Assert.Equal(HealthCheckRunOutcome.Skipped, await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None));
        VerifyNothingRecorded();
    }

    [Fact]
    public async Task DeletedOrDisabledCheck_IsSkipped()
    {
        Given(check: null, Activation());
        Assert.Equal(HealthCheckRunOutcome.Skipped, await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None));

        Given(Check(enabled: false), Activation());
        Assert.Equal(HealthCheckRunOutcome.Skipped, await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None));

        VerifyNothingRecorded();
    }

    [Fact]
    public async Task CompletedRun_PersistsRunAndUpdatesCheckStatus()
    {
        Given(Check(), Activation());
        var assertion = new HealthCheckAssertionResult { Op = HealthCheckAssertionOp.Contains, Value = "hello", Passed = false };
        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthCheckExecutionResult.Fail("Assertion failed", "Hi there", [assertion]));
        AgentHealthCheckRun? saved = null;
        _repository.Setup(r => r.InsertRunAsync(It.IsAny<AgentHealthCheckRun>())).Callback<AgentHealthCheckRun>(run => saved = run);

        var outcome = await Runner().RunAsync(TenantId, CheckId, "run-1", CancellationToken.None);

        Assert.Equal(HealthCheckRunOutcome.Recorded, outcome);
        Assert.NotNull(saved);
        Assert.Equal(HealthCheckStatus.Fail, saved!.Status);
        Assert.Equal(TenantId, saved.TenantId);
        Assert.Equal(CheckId, saved.CheckId);
        Assert.Equal(ActivationId, saved.ActivationId);
        Assert.Equal("Hi there", saved.ResponseSnippet);
        Assert.Equal("run-1", saved.WorkflowRunId);
        Assert.Single(saved.AssertionResults);
        _repository.Verify(r => r.RecordRunResultAsync(CheckId, HealthCheckStatus.Fail, saved.StartedAt, "Assertion failed"), Times.Once);
    }

    [Fact]
    public async Task CompletedRun_HandsThePreRunCheckAndTheRunToTheNotifier()
    {
        var check = Check();
        check.LastStatus = HealthCheckStatus.Pass;
        Given(check, Activation());
        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthCheckExecutionResult.Error("no worker"));

        await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None);

        _notifier.Verify(n => n.NotifyAsync(
            It.Is<AgentHealthCheck>(c => c.Id == CheckId && c.LastStatus == HealthCheckStatus.Pass),
            It.Is<AgentHealthCheckRun>(r => r.Status == HealthCheckStatus.Error && r.Detail == "no worker")), Times.Once);
    }

    [Fact]
    public async Task ExecutorException_IsRecordedAsError()
    {
        Given(Check(), Activation());
        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("temporal unreachable"));

        var outcome = await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None);

        Assert.Equal(HealthCheckRunOutcome.Recorded, outcome);
        _repository.Verify(r => r.InsertRunAsync(It.Is<AgentHealthCheckRun>(run =>
            run.Status == HealthCheckStatus.Error && run.Detail!.Contains("temporal unreachable"))), Times.Once);
    }

    [Fact]
    public async Task ExecutorCanceledByTimeout_IsRecordedAsError()
    {
        Given(Check(timeoutSeconds: 10), Activation());
        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None);

        _repository.Verify(r => r.InsertRunAsync(It.Is<AgentHealthCheckRun>(run =>
            run.Status == HealthCheckStatus.Error && run.Detail!.Contains("Timed out"))), Times.Once);
    }

    [Fact]
    public async Task LongDetail_IsTruncated()
    {
        Given(Check(), Activation());
        _executor
            .Setup(e => e.ExecuteAsync(It.IsAny<AgentHealthCheck>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(HealthCheckExecutionResult.Error(new string('x', 5000)));

        await Runner().RunAsync(TenantId, CheckId, null, CancellationToken.None);

        _repository.Verify(r => r.InsertRunAsync(It.Is<AgentHealthCheckRun>(run =>
            run.Detail!.Length == AgentHealthCheckRun.MaxDetailLength)), Times.Once);
    }

    [Fact]
    public void SystemPrincipal_ActsOnlyForTheCheckTenant()
    {
        var context = new Mock<ITenantContext>();
        context.SetupAllProperties();

        HealthCheckSystemPrincipal.Apply(context.Object, TenantId);

        Assert.Equal(TenantId, context.Object.TenantId);
        Assert.Equal(HealthCheckSystemPrincipal.UserId, context.Object.LoggedInUser);
        Assert.Equal([SystemRoles.TenantAdmin], context.Object.UserRoles);
        Assert.Equal([TenantId], context.Object.AuthorizedTenantIds);
    }
}
