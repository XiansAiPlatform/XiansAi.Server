namespace Shared.Services;

/// <summary>
/// Starts the webhook outbox write and the audit-log write together, without waiting for either.
/// Both operations snapshot request identity on this thread; the actual I/O runs in the background
/// so the originating business operation is not delayed.
/// </summary>
public static class DomainEventEmitter
{
    /// <summary>
    /// Emits a domain event to outbound webhooks and the audit log.
    /// </summary>
    /// <param name="webhookEventPublisher">Webhook outbox publisher.</param>
    /// <param name="auditLogService">Audit log writer.</param>
    /// <param name="eventType">One of the <see cref="Data.Models.DomainEventTypes"/> constants.</param>
    /// <param name="data">Event payload shared by the webhook envelope and the audit details.</param>
    /// <param name="tenantId">Owning tenant, when applicable.</param>
    /// <param name="activationName">Activation the action was performed against, when applicable.</param>
    public static void Emit(
        IWebhookEventPublisher webhookEventPublisher,
        IAuditLogService auditLogService,
        string eventType,
        object? data,
        string? tenantId = null,
        string? activationName = null)
    {
        ArgumentNullException.ThrowIfNull(webhookEventPublisher);
        ArgumentNullException.ThrowIfNull(auditLogService);

        // Kick both off on this thread so they snapshot ambient tenant/HTTP context while it is
        // still valid. Task.WhenAll lets their I/O overlap; we do not await it, because waiting
        // would still stall the write path by the slower of the two calls.
        _ = EmitConcurrentlyAsync(
            webhookEventPublisher.PublishAsync(eventType, data, tenantId),
            auditLogService.RecordEntryAsync(
                action: eventType,
                activationName: activationName,
                details: data));
    }

    private static async Task EmitConcurrentlyAsync(Task webhookTask, Task auditTask)
    {
        try
        {
            await Task.WhenAll(webhookTask, auditTask);
        }
        catch (Exception)
        {
            // Both callees swallow and log their own failures. This net exists only so an
            // unexpected exception cannot become an unobserved task exception.
        }
    }
}
