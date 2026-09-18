using Moq;
using Shared.Auditing;
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
            .Setup(a => a.RecordEntryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>()))
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
            a => a.RecordEntryAsync(
                DomainEventTypes.TenantCreated,
                DomainEventTypes.Describe(DomainEventTypes.TenantCreated),
                "activation-1",
                metadata,
                "acme"),
            Times.Once);

        Assert.False(webhookGate.Task.IsCompleted);
        Assert.False(auditGate.Task.IsCompleted);

        webhookGate.SetResult();
        auditGate.SetResult();
    }

    [Fact]
    public void Emit_WithoutTenantId_RecordsAuditAgainstPlatformTenant()
    {
        var webhook = new Mock<IWebhookEventPublisher>();
        webhook
            .Setup(w => w.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var audit = new Mock<IAuditLogService>();
        audit
            .Setup(a => a.RecordEntryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<AuditLogEntry>.Success(new AuditLogEntry
            {
                TenantId = AuditLogTenants.Platform,
                ParticipantId = "p",
                LoggedInUser = "u",
                Action = DomainEventTypes.UserSysAdminGranted
            }));

        var metadata = new { userId = "user-1", isSysAdmin = true };

        DomainEventEmitter.Emit(
            webhook.Object,
            audit.Object,
            DomainEventTypes.UserSysAdminGranted,
            metadata);

        webhook.Verify(
            w => w.PublishAsync(DomainEventTypes.UserSysAdminGranted, metadata, null),
            Times.Once);
        audit.Verify(
            a => a.RecordEntryAsync(
                DomainEventTypes.UserSysAdminGranted,
                DomainEventTypes.Describe(DomainEventTypes.UserSysAdminGranted),
                null,
                metadata,
                AuditLogTenants.Platform),
            Times.Once);
    }

    [Fact]
    public void Emit_UsesCallerDescription_WhenProvided()
    {
        var webhook = new Mock<IWebhookEventPublisher>();
        webhook
            .Setup(w => w.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var audit = new Mock<IAuditLogService>();
        audit
            .Setup(a => a.RecordEntryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<AuditLogEntry>.Success(new AuditLogEntry
            {
                TenantId = "acme",
                ParticipantId = "p",
                LoggedInUser = "u",
                Action = DomainEventTypes.TenantDisabled
            }));

        DomainEventEmitter.Emit(
            webhook.Object,
            audit.Object,
            DomainEventTypes.TenantDisabled,
            new { tenantId = "acme" },
            "acme",
            description: "Tenant 'Acme' (acme) was disabled.");

        audit.Verify(
            a => a.RecordEntryAsync(
                DomainEventTypes.TenantDisabled,
                "Tenant 'Acme' (acme) was disabled.",
                null,
                It.IsAny<object?>(),
                "acme"),
            Times.Once);
    }
}
