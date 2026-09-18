namespace Shared.Data.Models;

/// <summary>
/// Canonical identifiers for platform domain events. Shared by outbound webhooks and the audit
/// log: adding a constant here, then publishing/recording it from the relevant service, is enough
/// to emit both.
///
/// High-volume telemetry (per-message conversation writes, agent logs, activity history, usage
/// metrics, ephemeral cache writes, heartbeats) is intentionally NOT tracked here, as it would
/// overwhelm webhook listeners and the audit log.
/// </summary>
public static class DomainEventTypes
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

    /// <summary>A tenant's Temporal (flow-server) configuration was created or updated.</summary>
    public const string TenantTemporalUpdated = "tenant.temporal.updated";

    /// <summary>A tenant's Temporal (flow-server) configuration was reverted to the platform default.</summary>
    public const string TenantTemporalReverted = "tenant.temporal.reverted";

    /// <summary>The platform was bootstrapped (first SysAdmin, tenant, and API key).</summary>
    public const string PlatformBootstrapped = "platform.bootstrapped";

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

    /// <summary>A user account was permanently deleted.</summary>
    public const string UserDeleted = "user.deleted";

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

    /// <summary>A tenant-scoped agent was promoted into a new system-scoped template.</summary>
    public const string AgentTemplatePromoted = "agent.template.promoted";

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
