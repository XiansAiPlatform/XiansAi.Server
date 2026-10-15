using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Services;
using Shared.Services.HealthChecks;

namespace Tests.UnitTests.Shared.Services.HealthChecks;

public class HealthCheckNotifierTests
{
    private const string TenantId = "acme";
    private const string OpsId = "66f000000000000000000010";
    private const string DevId = "66f000000000000000000011";
    private const string OtherId = "66f000000000000000000012";

    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IEmailService> _email = new();

    public HealthCheckNotifierTests()
    {
        _email.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(true);
        _contacts.Setup(c => c.ListAsync(TenantId, null)).ReturnsAsync(
        [
            Contact(OpsId, "Ops", "ops@acme.com"),
            Contact(DevId, "Dev", "dev@acme.com"),
            Contact(OtherId, "Other", "other@acme.com")
        ]);
    }

    private HealthCheckNotifier Notifier() => new(_contacts.Object, _email.Object, NullLogger<HealthCheckNotifier>.Instance);

    private static Contact Contact(string id, string name, string email) =>
        new() { Id = id, TenantId = TenantId, Name = name, Email = email, CreatedBy = "admin" };

    private static AgentHealthCheck Check(HealthCheckStatus? lastStatus, int consecutiveFailures = 0, params string[] contactIds) => new()
    {
        Id = "66f000000000000000000001",
        TenantId = TenantId,
        ActivationId = "66f000000000000000000002",
        AgentName = "Support Agent",
        ActivationName = "prod",
        Name = "health ping",
        Type = HealthCheckType.Webhook,
        IntervalMinutes = 15,
        CreatedBy = "admin",
        LastStatus = lastStatus,
        ConsecutiveFailures = consecutiveFailures,
        NotifyContactIds = contactIds.ToList()
    };

    private static AgentHealthCheckRun Run(HealthCheckStatus status, string? detail = null, string? snippet = null) => new()
    {
        TenantId = TenantId,
        CheckId = "66f000000000000000000001",
        ActivationId = "66f000000000000000000002",
        Type = HealthCheckType.Webhook,
        Status = status,
        StartedAt = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc),
        Detail = detail,
        ResponseSnippet = snippet
    };

    private void VerifyNoEmail() =>
        _email.Verify(e => e.SendEmailAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

    [Theory]
    [InlineData(null, HealthCheckStatus.Fail, HealthCheckNotificationKind.Failing)]
    [InlineData(null, HealthCheckStatus.Error, HealthCheckNotificationKind.Failing)]
    [InlineData(HealthCheckStatus.Pass, HealthCheckStatus.Fail, HealthCheckNotificationKind.Failing)]
    [InlineData(HealthCheckStatus.Pass, HealthCheckStatus.Error, HealthCheckNotificationKind.Failing)]
    [InlineData(HealthCheckStatus.Fail, HealthCheckStatus.Pass, HealthCheckNotificationKind.Recovered)]
    [InlineData(HealthCheckStatus.Error, HealthCheckStatus.Pass, HealthCheckNotificationKind.Recovered)]
    [InlineData(null, HealthCheckStatus.Pass, HealthCheckNotificationKind.None)]
    [InlineData(HealthCheckStatus.Pass, HealthCheckStatus.Pass, HealthCheckNotificationKind.None)]
    [InlineData(HealthCheckStatus.Fail, HealthCheckStatus.Fail, HealthCheckNotificationKind.None)]
    [InlineData(HealthCheckStatus.Fail, HealthCheckStatus.Error, HealthCheckNotificationKind.None)]
    [InlineData(HealthCheckStatus.Error, HealthCheckStatus.Fail, HealthCheckNotificationKind.None)]
    public void Policy_NotifiesOnStateChangesOnly(HealthCheckStatus? previous, HealthCheckStatus current, HealthCheckNotificationKind expected)
    {
        Assert.Equal(expected, HealthCheckNotificationPolicy.Decide(previous, current));
    }

    [Fact]
    public async Task FirstFailure_EmailsEachSelectedContactSeparately()
    {
        await Notifier().NotifyAsync(
            Check(HealthCheckStatus.Pass, 0, OpsId, DevId),
            Run(HealthCheckStatus.Fail, "Assertion failed: expected $.status to equal 'ok' but got degraded", "{\"status\":\"degraded\"}"));

        _email.Verify(e => e.SendEmailAsync(TenantId, "ops@acme.com",
            "Eval failing: health ping (Support Agent / prod)",
            It.Is<string>(body => body.StartsWith("Hi Ops,") && body.Contains("got degraded") && body.Contains("{\"status\":\"degraded\"}")),
            false), Times.Once);
        _email.Verify(e => e.SendEmailAsync(TenantId, "dev@acme.com", It.IsAny<string>(), It.Is<string>(body => body.StartsWith("Hi Dev,")), false), Times.Once);
        _email.Verify(e => e.SendEmailAsync(TenantId, "other@acme.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Recovery_EmailsWithTheFailedRunCount()
    {
        await Notifier().NotifyAsync(Check(HealthCheckStatus.Error, 3, OpsId), Run(HealthCheckStatus.Pass));

        _email.Verify(e => e.SendEmailAsync(TenantId, "ops@acme.com",
            "Eval recovered: health ping (Support Agent / prod)",
            It.Is<string>(body => body.Contains("passing again after 3 failed runs")),
            false), Times.Once);
    }

    [Fact]
    public async Task ErrorRun_SaysTheAgentCouldNotBeReached()
    {
        await Notifier().NotifyAsync(Check(null, 0, OpsId), Run(HealthCheckStatus.Error, "Timed out after 30s"));

        _email.Verify(e => e.SendEmailAsync(TenantId, "ops@acme.com", It.IsAny<string>(),
            It.Is<string>(body => body.Contains("could not be reached") && body.Contains("Timed out after 30s")), false), Times.Once);
    }

    [Fact]
    public async Task StillFailing_SendsNothing()
    {
        await Notifier().NotifyAsync(Check(HealthCheckStatus.Fail, 2, OpsId), Run(HealthCheckStatus.Error));
        VerifyNoEmail();
    }

    [Fact]
    public async Task NoContactsSelected_SendsNothing()
    {
        await Notifier().NotifyAsync(Check(HealthCheckStatus.Pass), Run(HealthCheckStatus.Fail));

        VerifyNoEmail();
        _contacts.Verify(c => c.ListAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task EmailOff_SendsNothing()
    {
        _email.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(false);

        await Notifier().NotifyAsync(Check(HealthCheckStatus.Pass, 0, OpsId), Run(HealthCheckStatus.Fail));

        VerifyNoEmail();
    }

    [Fact]
    public async Task DeletedContact_IsSkipped()
    {
        await Notifier().NotifyAsync(Check(HealthCheckStatus.Pass, 0, "66f0000000000000000000ff", OpsId), Run(HealthCheckStatus.Fail));

        _email.Verify(e => e.SendEmailAsync(TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task OneFailedSend_DoesNotStopTheOthersOrThrow()
    {
        _email.Setup(e => e.SendEmailAsync(TenantId, "ops@acme.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        await Notifier().NotifyAsync(Check(HealthCheckStatus.Pass, 0, OpsId, DevId), Run(HealthCheckStatus.Fail));

        _email.Verify(e => e.SendEmailAsync(TenantId, "dev@acme.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task ContactLookupFailure_DoesNotThrow()
    {
        _contacts.Setup(c => c.ListAsync(TenantId, null)).ThrowsAsync(new TimeoutException("mongo"));

        await Notifier().NotifyAsync(Check(HealthCheckStatus.Pass, 0, OpsId), Run(HealthCheckStatus.Fail));

        VerifyNoEmail();
    }
}
