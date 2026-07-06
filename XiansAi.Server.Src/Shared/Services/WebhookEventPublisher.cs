using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Publishes server events to the webhook outbox. Each event is expanded into one delivery row
/// per matching configured subscription; a background dispatcher performs the actual HTTP
/// delivery. Publishing never throws to the caller and does not block on the database, so a
/// failure or slowdown here cannot break or delay the originating business operation.
/// </summary>
public interface IWebhookEventPublisher
{
    /// <summary>
    /// Queues a webhook event for delivery to every enabled subscription that matches
    /// <paramref name="eventType"/>. No-op when webhooks are disabled or nothing matches.
    /// The returned task completes as soon as the delivery rows are built; the actual outbox
    /// write to the database happens in the background (best-effort, fire-and-forget) so the
    /// caller is never blocked by database latency.
    /// </summary>
    /// <param name="eventType">One of the <see cref="WebhookEventTypes"/> constants.</param>
    /// <param name="data">Event-specific payload placed under the envelope's <c>data</c> field.</param>
    /// <param name="tenantId">Owning tenant, when applicable.</param>
    Task PublishAsync(string eventType, object? data, string? tenantId = null);
}

public class WebhookEventPublisher : IWebhookEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WebhooksOptions _options;
    private readonly IWebhookDeliveryRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WebhookEventPublisher> _logger;

    public WebhookEventPublisher(
        WebhooksOptions options,
        IWebhookDeliveryRepository repository,
        ITenantContext tenantContext,
        ILogger<WebhookEventPublisher> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task PublishAsync(string eventType, object? data, string? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(eventType) || !_options.Enabled)
        {
            return Task.CompletedTask;
        }

        var matching = _options.Subscriptions
            .Where(s => s.MatchesEvent(eventType))
            .ToList();

        if (matching.Count == 0)
        {
            return Task.CompletedTask;
        }

        List<WebhookDelivery> deliveries;
        try
        {
            // Build the delivery rows on the caller's thread: this snapshots the ambient tenant/
            // actor context (only valid here) and serializes the payload. It is fast, CPU-only work
            // with no I/O. Any failure is logged and swallowed so it cannot break the business op.
            deliveries = BuildDeliveries(eventType, data, tenantId, matching);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build webhook deliveries for event {EventType}", eventType);
            return Task.CompletedTask;
        }

        // Persist the outbox rows in the background. We deliberately do not await this: the write
        // (with its retries) must not add latency to, or fail, the originating business operation.
        // This trades a small durability window for isolation — publishing is best-effort.
        _ = PersistDeliveriesAsync(deliveries, eventType);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Expands an event into one delivery row per matching subscription, sharing a single event id
    /// and serialized payload across them.
    /// </summary>
    private List<WebhookDelivery> BuildDeliveries(
        string eventType, object? data, string? tenantId, IReadOnlyList<WebhookSubscriptionOptions> matching)
    {
        var now = DateTime.UtcNow;
        var eventId = Guid.NewGuid().ToString();
        var actor = BuildActor();

        var envelope = new WebhookEventEnvelope
        {
            EventType = eventType,
            EventId = eventId,
            TenantId = tenantId,
            OccurredAt = now,
            Actor = actor,
            Data = data
        };

        var payload = JsonSerializer.Serialize(envelope, SerializerOptions);

        return matching
            .Select(subscription => new WebhookDelivery
            {
                Id = ObjectId.GenerateNewId().ToString(),
                EventId = eventId,
                EventType = eventType,
                SubscriptionName = subscription.Name,
                TenantId = tenantId,
                ActorUserId = actor?.UserId,
                ActorUserType = actor?.UserType,
                Payload = payload,
                Status = WebhookDeliveryStatus.Pending,
                AttemptCount = 0,
                CreatedAt = now,
                NextAttemptAt = now
            })
            .ToList();
    }

    /// <summary>
    /// Writes the delivery rows to the outbox off the request path. Runs as a fire-and-forget task,
    /// so it must never throw: any failure is logged and the event is dropped (best-effort). Safe to
    /// use the injected repository here because it holds no per-request state (only a thread-safe,
    /// long-lived Mongo collection) and is not disposed when the request scope ends.
    /// </summary>
    private async Task PersistDeliveriesAsync(IReadOnlyList<WebhookDelivery> deliveries, string eventType)
    {
        try
        {
            await _repository.InsertManyAsync(deliveries);

            _logger.LogInformation(
                "Queued {Count} webhook delivery(s) for event {EventType} ({EventId})",
                deliveries.Count, eventType, deliveries[0].EventId);
        }
        catch (Exception ex)
        {
            // Never propagate: webhook publishing is best-effort relative to the business operation.
            _logger.LogError(ex, "Failed to queue webhook deliveries for event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Snapshots the current authenticated principal for auditing. Returns null when no user
    /// context is available (e.g. a system-originated event).
    /// </summary>
    private WebhookActor? BuildActor()
    {
        var userId = _tenantContext.LoggedInUser;
        var roles = _tenantContext.UserRoles;
        var actorTenantId = _tenantContext.TenantId;

        var hasUser = !string.IsNullOrWhiteSpace(userId);
        var hasRoles = roles != null && roles.Length > 0;
        if (!hasUser && !hasRoles)
        {
            return null;
        }

        return new WebhookActor
        {
            UserId = hasUser ? userId : null,
            UserType = _tenantContext.UserType.ToString(),
            TenantId = string.IsNullOrWhiteSpace(actorTenantId) ? null : actorTenantId,
            Roles = hasRoles ? roles : null
        };
    }
}
