using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Canonical event type identifiers for outbound webhooks. This is the catalog of all trackable
/// events. Adding a new event is as simple as declaring a constant here and calling the publisher
/// from the relevant service.
///
/// High-volume telemetry (per-message conversation writes, agent logs, activity history, usage
/// metrics, ephemeral cache writes, heartbeats) is intentionally NOT tracked here, as it would
/// overwhelm webhook listeners.
/// </summary>
public static class WebhookEventTypes
{
    // ----- Tenant lifecycle -----

    /// <summary>A new tenant was created.</summary>
    public const string TenantCreated = "tenant.created";

    /// <summary>A tenant's profile (name/domain/description/theme/logo/timezone) was updated.</summary>
    public const string TenantUpdated = "tenant.updated";

    /// <summary>A tenant was enabled.</summary>
    public const string TenantEnabled = "tenant.enabled";

    /// <summary>A tenant was disabled.</summary>
    public const string TenantDisabled = "tenant.disabled";

    /// <summary>A tenant was deleted.</summary>
    public const string TenantDeleted = "tenant.deleted";

    /// <summary>A tenant's OIDC configuration was created or updated.</summary>
    public const string TenantOidcUpdated = "tenant.oidc.updated";

    /// <summary>A tenant's OIDC configuration was deleted.</summary>
    public const string TenantOidcDeleted = "tenant.oidc.deleted";

    // ----- User lifecycle (tenant-scoped and global) -----

    /// <summary>A brand-new user account was created.</summary>
    public const string UserCreated = "user.created";

    /// <summary>An existing user account was granted membership in a tenant.</summary>
    public const string UserTenantAdded = "user.tenant.added";

    /// <summary>A user's membership in a tenant was removed.</summary>
    public const string UserTenantRemoved = "user.tenant.removed";

    /// <summary>A user's profile (name/email) was updated.</summary>
    public const string UserUpdated = "user.updated";

    /// <summary>A user's tenant membership was approved.</summary>
    public const string UserApproved = "user.approved";

    /// <summary>A user's tenant membership approval was revoked.</summary>
    public const string UserUnapproved = "user.unapproved";

    /// <summary>A role was added to a user within a tenant.</summary>
    public const string UserRoleChanged = "user.role.changed";

    /// <summary>A role was removed from a user within a tenant.</summary>
    public const string UserRoleRemoved = "user.role.removed";

    /// <summary>A user was granted the system administrator flag.</summary>
    public const string UserSysAdminGranted = "user.sysadmin.granted";

    /// <summary>A user's system administrator flag was revoked.</summary>
    public const string UserSysAdminRevoked = "user.sysadmin.revoked";

    /// <summary>A user account was enabled (unlocked).</summary>
    public const string UserEnabled = "user.enabled";

    /// <summary>A user account was disabled (locked out).</summary>
    public const string UserDisabled = "user.disabled";

    // ----- Agents, deployments and templates -----

    /// <summary>An agent was registered for the first time (via the Agent API).</summary>
    public const string AgentRegistered = "agent.registered";

    /// <summary>An agent and its dependent resources were deleted.</summary>
    public const string AgentDeleted = "agent.deleted";

    /// <summary>An agent deployment's configuration was updated (via the Admin API).</summary>
    public const string AgentDeploymentUpdated = "agent.deployment.updated";

    /// <summary>Ownership of an agent was transferred to another user.</summary>
    public const string AgentOwnershipTransferred = "agent.ownership.transferred";

    /// <summary>A system template agent was deployed into a tenant.</summary>
    public const string AgentTemplateDeployed = "agent.template.deployed";

    /// <summary>A system-scoped template agent's metadata was updated.</summary>
    public const string TemplateUpdated = "template.updated";

    /// <summary>A system-scoped template agent was deleted.</summary>
    public const string TemplateDeleted = "template.deleted";

    // ----- Flow definitions -----

    /// <summary>A new flow (workflow) definition was registered.</summary>
    public const string FlowDefinitionCreated = "flow.definition.created";

    /// <summary>An existing flow (workflow) definition was updated (hash changed).</summary>
    public const string FlowDefinitionUpdated = "flow.definition.updated";

    // ----- Activations -----

    /// <summary>An agent activation was created.</summary>
    public const string ActivationCreated = "activation.created";

    /// <summary>An agent activation was updated.</summary>
    public const string ActivationUpdated = "activation.updated";

    /// <summary>An agent activation was activated (workflows started).</summary>
    public const string ActivationActivated = "activation.activated";

    /// <summary>An agent activation was deactivated.</summary>
    public const string ActivationDeactivated = "activation.deactivated";

    /// <summary>An agent activation was deleted.</summary>
    public const string ActivationDeleted = "activation.deleted";

    // ----- Knowledge -----

    /// <summary>A knowledge item (or override/version) was created.</summary>
    public const string KnowledgeCreated = "knowledge.created";

    /// <summary>A knowledge item was updated (new version).</summary>
    public const string KnowledgeUpdated = "knowledge.updated";

    /// <summary>A knowledge item was deleted.</summary>
    public const string KnowledgeDeleted = "knowledge.deleted";

    // ----- Secrets -----

    /// <summary>A vault secret was created.</summary>
    public const string SecretCreated = "secret.created";

    /// <summary>A vault secret was updated.</summary>
    public const string SecretUpdated = "secret.updated";

    /// <summary>A vault secret was deleted.</summary>
    public const string SecretDeleted = "secret.deleted";

    // ----- API keys and certificates -----

    /// <summary>An API key was created.</summary>
    public const string ApiKeyCreated = "apikey.created";

    /// <summary>An API key was revoked.</summary>
    public const string ApiKeyRevoked = "apikey.revoked";

    /// <summary>An API key was rotated.</summary>
    public const string ApiKeyRotated = "apikey.rotated";

    /// <summary>A client certificate was issued.</summary>
    public const string CertificateCreated = "certificate.created";

    /// <summary>A client certificate was revoked.</summary>
    public const string CertificateRevoked = "certificate.revoked";

    // ----- App integrations -----

    /// <summary>An app integration was created.</summary>
    public const string IntegrationCreated = "integration.created";

    /// <summary>An app integration was updated.</summary>
    public const string IntegrationUpdated = "integration.updated";

    /// <summary>An app integration was deleted.</summary>
    public const string IntegrationDeleted = "integration.deleted";

    /// <summary>An app integration was enabled.</summary>
    public const string IntegrationEnabled = "integration.enabled";

    /// <summary>An app integration was disabled.</summary>
    public const string IntegrationDisabled = "integration.disabled";

    /// <summary>A builtin webhook integration was created. (Deletion emits the generic integration.deleted event.)</summary>
    public const string IntegrationWebhookCreated = "integration.webhook.created";
}

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
