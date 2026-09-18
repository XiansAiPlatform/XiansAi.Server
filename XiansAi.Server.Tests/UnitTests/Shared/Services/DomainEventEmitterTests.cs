using Moq;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class DomainEventEmitterTests
{
    [Fact]
    public void Emit_StartsWebhookAndAuditWithoutWaitingForEither()
    {
        var webhookGate = new TaskCompletionSource();
        var auditGate = new TaskCompletionSource();

        var webhook = new Mock<IWebhookEventPublisher>();
        webhook
            .Setup(w => w.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(webhookGate.Task);

        var audit = new Mock<IAuditLogService>();
        audit
            .Setup(a => a.RecordEntryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>()))
            .Returns(async () =>
            {
                await auditGate.Task;
                return ServiceResult<AuditLogEntry>.Success(new AuditLogEntry
                {
                    TenantId = "t",
                    ParticipantId = "p",
                    LoggedInUser = "u",
                    Action = "tenant.created"
                });
            });

        var metadata = new { tenantId = "acme" };

        DomainEventEmitter.Emit(
            webhook.Object,
            audit.Object,
            DomainEventTypes.TenantCreated,
            metadata,
            "acme",
            activationName: "activation-1");

        webhook.Verify(
            w => w.PublishAsync(DomainEventTypes.TenantCreated, metadata, "acme"),
            Times.Once);
        audit.Verify(
            a => a.RecordEntryAsync(DomainEventTypes.TenantCreated, null, "activation-1", metadata),
            Times.Once);

        Assert.False(webhookGate.Task.IsCompleted);
        Assert.False(auditGate.Task.IsCompleted);

        webhookGate.SetResult();
        auditGate.SetResult();
    }
}
