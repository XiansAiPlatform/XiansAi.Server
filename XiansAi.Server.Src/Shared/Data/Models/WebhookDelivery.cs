using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Lifecycle state of a single webhook delivery row.
/// </summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting to be delivered (or waiting for the next retry).</summary>
    Pending,

    /// <summary>Claimed by an instance and currently being delivered.</summary>
    Delivering,

    /// <summary>Successfully delivered (listener returned a 2xx response).</summary>
    Delivered,

    /// <summary>Permanently failed after exhausting the maximum number of attempts.</summary>
    Failed
}

/// <summary>
/// Identifies the authenticated principal that triggered an event, for auditing.
/// </summary>
public class WebhookActor
{
    /// <summary>The acting user's id (from the authenticated context), when known.</summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    /// <summary>How the actor authenticated (e.g. UserToken, UserApiKey, AgentApiKey).</summary>
    [JsonPropertyName("userType")]
    public string? UserType { get; set; }

    /// <summary>The tenant the actor was operating in (may differ from the event's target tenant).</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>The actor's roles at the time of the action.</summary>
    [JsonPropertyName("roles")]
    public string[]? Roles { get; set; }
}

/// <summary>
/// The JSON body POSTed to a webhook listener. <see cref="Data"/> carries event-specific fields.
/// </summary>
public class WebhookEventEnvelope
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; set; }

    /// <summary>Unique id of the logical event; identical across all subscription deliveries.</summary>
    [JsonPropertyName("eventId")]
    public required string EventId { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }

    /// <summary>Who triggered the event (for auditing). Null for system-originated events.</summary>
    [JsonPropertyName("actor")]
    public WebhookActor? Actor { get; set; }

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// Outbox row representing the delivery of one event to one subscription, persisted in the
/// MongoDB collection <c>webhook_deliveries</c>. A background dispatcher atomically claims due
/// rows so exactly one server instance delivers each one, then retries on failure.
/// </summary>
[BsonIgnoreExtraElements]
public class WebhookDelivery
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Logical event id, shared across all subscription deliveries for the same event.</summary>
    [BsonElement("event_id")]
    public required string EventId { get; set; }

    [BsonElement("event_type")]
    public required string EventType { get; set; }

    /// <summary>Name of the configured subscription this delivery targets.</summary>
    [BsonElement("subscription_name")]
    public required string SubscriptionName { get; set; }

    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }

    /// <summary>Id of the user who triggered the event (for auditing). Null for system events.</summary>
    [BsonElement("actor_user_id")]
    public string? ActorUserId { get; set; }

    /// <summary>How the acting user authenticated (for auditing).</summary>
    [BsonElement("actor_user_type")]
    public string? ActorUserType { get; set; }

    /// <summary>JSON-serialized <see cref="WebhookEventEnvelope"/> sent as the request body.</summary>
    [BsonElement("payload")]
    public required string Payload { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    [BsonElement("attempt_count")]
    public int AttemptCount { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Earliest time this delivery may be (re)attempted.</summary>
    [BsonElement("next_attempt_at")]
    public DateTime NextAttemptAt { get; set; }

    /// <summary>When set and in the future, this delivery is leased to <see cref="ClaimedBy"/>.</summary>
    [BsonElement("lease_expires_at")]
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>Identifier of the instance currently holding the lease.</summary>
    [BsonElement("claimed_by")]
    public string? ClaimedBy { get; set; }

    [BsonElement("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [BsonElement("last_error")]
    public string? LastError { get; set; }
}
