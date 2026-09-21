namespace Features.AdminApi.Auth;

/// <summary>
/// One named action the AdminApi capability matrix can guard. 
/// Each action has a default role set, which is what the action allowed before it was migrated onto the matrix.
/// 
/// <c>DefaultRoles</c> is used whenever no row is stored including on a fresh or wiped database. 
/// 
/// <c>NonDelegable</c> marks an action whose <c>DefaultRoles</c> is permanently fixed.
/// </summary>
public sealed record CapabilityAction(
    string Name,
    IReadOnlyList<string> DefaultRoles,
    string Description,
    bool NonDelegable = false);

/// <summary>
/// The actions guarded by the capability matrix. Named after the operation rather than the route, so
/// renaming a route or adding a second one for the same operation needs no matrix edit. 
/// </summary>
public static class CapabilityActions
{
    // AdminUserEndpoints: /tenants/{tenantId}/users
    public const string TenantUsersList = "tenant.users.list";
    public const string TenantUsersGet = "tenant.users.get";
    public const string TenantUsersCreate = "tenant.users.create";
    public const string TenantUsersUpdate = "tenant.users.update";
    public const string TenantUsersDelete = "tenant.users.delete";
    public const string TenantUsersRolesRemove = "tenant.users.roles.remove";

    // AdminGlobalUserEndpoints: /users
    public const string GlobalUsersList = "global.users.list";
    public const string GlobalUsersGet = "global.users.get";
    public const string GlobalUsersUpdate = "global.users.update";
    public const string GlobalUsersSysAdminSet = "global.users.sysadmin.set";
    public const string GlobalUsersStatusSet = "global.users.status.set";
    public const string GlobalUsersDelete = "global.users.delete";

    // AdminAgentActivationEndpoints: /tenants/{tenantId}/agentActivations
    public const string TenantAgentActivationsList = "tenant.agentActivations.list";
    public const string TenantAgentActivationsGet = "tenant.agentActivations.get";
    public const string TenantAgentActivationsCreate = "tenant.agentActivations.create";
    public const string TenantAgentActivationsUpdate = "tenant.agentActivations.update";
    public const string TenantAgentActivationsActivate = "tenant.agentActivations.activate";
    public const string TenantAgentActivationsDeactivate = "tenant.agentActivations.deactivate";
    public const string TenantAgentActivationsDelete = "tenant.agentActivations.delete";

    // AdminDataEndpoints: /tenants/{tenantId}/data
    public const string TenantDataSchema = "tenant.data.schema";
    public const string TenantDataList = "tenant.data.list";
    public const string TenantDataDelete = "tenant.data.delete";
    public const string TenantDataDeleteRecord = "tenant.data.deleteRecord";
    public const string TenantDataDeleteByActivation = "tenant.data.deleteByActivation";

    // AdminFeedbackEndpoints: /tenants/{tenantId}/feedback
    public const string TenantFeedbackSubmit = "tenant.feedback.submit";
    public const string TenantFeedbackStats = "tenant.feedback.stats";
    public const string TenantFeedbackList = "tenant.feedback.list";
    public const string TenantFeedbackGet = "tenant.feedback.get";
    public const string TenantFeedbackDeleteByActivation = "tenant.feedback.deleteByActivation";

    // AdminHeartbeatEndpoints: /tenants/{tenantId}/heartbeat
    public const string TenantHeartbeatCheck = "tenant.heartbeat.check";

    // AdminLogsEndpoints: /tenants/{tenantId}/logs
    public const string TenantLogsStreams = "tenant.logs.streams";
    public const string TenantLogsList = "tenant.logs.list";
    public const string TenantLogsDeleteByActivation = "tenant.logs.deleteByActivation";

    // AdminMessagingEndpoints: /tenants/{tenantId}/messaging
    public const string TenantMessagingListen = "tenant.messaging.listen";
    public const string TenantMessagingSend = "tenant.messaging.send";
    public const string TenantMessagingSendFile = "tenant.messaging.sendFile";
    public const string TenantMessagingDownloadFile = "tenant.messaging.downloadFile";
    public const string TenantMessagingTopics = "tenant.messaging.topics";
    public const string TenantMessagingHistory = "tenant.messaging.history";
    public const string TenantMessagingDeleteByTopic = "tenant.messaging.deleteByTopic";
    public const string TenantMessagingDeleteByActivation = "tenant.messaging.deleteByActivation";

    // AdminMetricsEndpoints: /tenants/{tenantId}/metrics
    public const string TenantMetricsStats = "tenant.metrics.stats";
    public const string TenantMetricsTimeSeries = "tenant.metrics.timeseries";
    public const string TenantMetricsCategories = "tenant.metrics.categories";
    public const string TenantMetricsDeleteByActivation = "tenant.metrics.deleteByActivation";

    // AdminScheduleEndpoints: /tenants/{tenantId}/agents/{agentName}/schedules
    public const string TenantSchedulesList = "tenant.schedules.list";
    public const string TenantSchedulesGet = "tenant.schedules.get";
    public const string TenantSchedulesUpcomingRuns = "tenant.schedules.upcomingRuns";
    public const string TenantSchedulesHistory = "tenant.schedules.history";
    public const string TenantSchedulesPause = "tenant.schedules.pause";
    public const string TenantSchedulesResume = "tenant.schedules.resume";
    public const string TenantSchedulesDelete = "tenant.schedules.delete";
    public const string TenantSchedulesDeleteAll = "tenant.schedules.deleteAll";
    public const string TenantSchedulesDeleteByActivation = "tenant.schedules.deleteByActivation";

    // AdminStatsEndpoints: /tenants/{tenantId}/stats
    public const string TenantStatsGet = "tenant.stats.get";

    // AdminTaskEndpoints: /tenants/{tenantId}/tasks
    public const string TenantTasksList = "tenant.tasks.list";
    public const string TenantTasksGet = "tenant.tasks.get";
    public const string TenantTasksUpdateDraft = "tenant.tasks.updateDraft";
    public const string TenantTasksUpdateMetadata = "tenant.tasks.updateMetadata";
    public const string TenantTasksPerformAction = "tenant.tasks.performAction";

    // WorkflowManagementEndpoints: /tenants/{tenantId}/workflows
    public const string TenantWorkflowsActivate = "tenant.workflows.activate";
    public const string TenantWorkflowsGet = "tenant.workflows.get";
    public const string TenantWorkflowsList = "tenant.workflows.list";
    public const string TenantWorkflowsEvents = "tenant.workflows.events";
    public const string TenantWorkflowsEventsStream = "tenant.workflows.eventsStream";
    public const string TenantWorkflowsTypes = "tenant.workflows.types";
    public const string TenantWorkflowsCancel = "tenant.workflows.cancel";

    // AdminAgentDeploymentEndpoints: /tenants/{tenantId}/agentDeployments
    public const string TenantAgentDeploymentsList = "tenant.agentDeployments.list";
    public const string TenantAgentDeploymentsGet = "tenant.agentDeployments.get";
    public const string TenantAgentDeploymentsUpdate = "tenant.agentDeployments.update";
    public const string TenantAgentDeploymentsDelete = "tenant.agentDeployments.delete";
    public const string TenantAgentDeploymentsPromoteToTemplate = "tenant.agentDeployments.promoteToTemplate";

    // AdminWorkerDeploymentEndpoints: /tenants/{tenantId}/worker-deployments
    public const string TenantWorkerDeploymentsList = "tenant.workerDeployments.list";
    public const string TenantWorkerDeploymentsGet = "tenant.workerDeployments.get";
    public const string TenantWorkerDeploymentsSetCurrentVersion = "tenant.workerDeployments.setCurrentVersion";
    public const string TenantWorkerDeploymentsSetRampingVersion = "tenant.workerDeployments.setRampingVersion";

    // AdminParticipantsEndpoints: /participants
    public const string GlobalParticipantsGetByEmail = "global.participants.getByEmail";
    public const string GlobalParticipantsGetByUserId = "global.participants.getByUserId";

    // AdminTenantEndpoints: /tenants (tenant management itself, not any one tenant's data)
    public const string TenantsList = "tenants.list";
    public const string TenantsGet = "tenants.get";
    public const string TenantsCreate = "tenants.create";
    public const string TenantsUpdate = "tenants.update";
    public const string TenantsDelete = "tenants.delete";
    public const string TenantsMetadataList = "tenants.metadata.list";
    public const string TenantsMetadataGet = "tenants.metadata.get";
    public const string TenantsMetadataUpsert = "tenants.metadata.upsert";
    public const string TenantsMetadataDelete = "tenants.metadata.delete";


    public const string TenantLogoGet = "tenant.logo.get";
    public const string TenantLogoSet = "tenant.logo.set";
    public const string TenantLogoClear = "tenant.logo.clear";
    public const string TenantThemeGet = "tenant.theme.get";
    public const string TenantThemeSet = "tenant.theme.set";
    public const string TenantThemeClear = "tenant.theme.clear";

    // AdminTenantEndpoints: /tenants/{tenantId}/temporal-config
    public const string TenantTemporalConfigGet = "tenant.temporalConfig.get";
    public const string TenantTemporalConfigSet = "tenant.temporalConfig.set";
    public const string TenantTemporalConfigRevert = "tenant.temporalConfig.revert";
    public const string TenantTemporalConfigTestConnection = "tenant.temporalConfig.testConnection";

    // AdminTenantEndpoints: /tenants/{tenantId}/oidc-config
    public const string TenantOidcConfigGet = "tenant.oidcConfig.get";
    public const string TenantOidcConfigUpsert = "tenant.oidcConfig.upsert";
    public const string TenantOidcConfigDelete = "tenant.oidcConfig.delete";
    public const string TenantOidcConfigTemplate = "tenant.oidcConfig.template";

    // AdminApiKeyEndpoints: /tenants/{tenantId}/agent-certificates
    public const string TenantAgentCertificatesGenerate = "tenant.agentCertificates.generate";
    public const string TenantAgentCertificatesList = "tenant.agentCertificates.list";
    public const string TenantAgentCertificatesRevoke = "tenant.agentCertificates.revoke";

    // AdminApiKeyEndpoints: /tenants/{tenantId}/admin-apikeys
    public const string TenantAdminApiKeysCreate = "tenant.adminApiKeys.create";
    public const string TenantAdminApiKeysList = "tenant.adminApiKeys.list";
    public const string TenantAdminApiKeysGet = "tenant.adminApiKeys.get";
    public const string TenantAdminApiKeysRevoke = "tenant.adminApiKeys.revoke";
    public const string TenantAdminApiKeysRotate = "tenant.adminApiKeys.rotate";

    // AdminAppIntegrationEndpoints: /integrations/metadata
    // Not route-nested under /tenants/{tenantId} and resolves no tenant at all — static,
    // platform-wide catalog data — so this is global.*, not tenant.*.
    public const string GlobalIntegrationMetadataList = "global.integrationMetadata.list";

    // AdminAppIntegrationEndpoints: /tenants/{tenantId}/webhooks
    public const string TenantWebhooksCreate = "tenant.webhooks.create";
    public const string TenantWebhooksList = "tenant.webhooks.list";
    public const string TenantWebhooksDeleteByActivation = "tenant.webhooks.deleteByActivation";
    public const string TenantWebhooksDelete = "tenant.webhooks.delete";

    // AdminAppIntegrationEndpoints: /tenants/{tenantId}/integrations
    public const string TenantIntegrationsList = "tenant.integrations.list";
    public const string TenantIntegrationsGet = "tenant.integrations.get";
    public const string TenantIntegrationsCreate = "tenant.integrations.create";
    public const string TenantIntegrationsUpdate = "tenant.integrations.update";
    public const string TenantIntegrationsDelete = "tenant.integrations.delete";
    public const string TenantIntegrationsEnable = "tenant.integrations.enable";
    public const string TenantIntegrationsDisable = "tenant.integrations.disable";
    public const string TenantIntegrationsTest = "tenant.integrations.test";
    public const string TenantIntegrationsWebhookUrl = "tenant.integrations.webhookUrl";

    // AdminSecretVaultEndpoints: /secrets
    // Not route-nested under /tenants/{tenantId} like the rest of tenant.* — this group resolves
    // its effective tenant dynamically per-request (body/query) via SecretVaultScopeEnforcement
    // instead. Named tenant.secrets.* anyway: the action is still tenant-scoped in effect, just not
    // in the route shape the tenant.* convention above otherwise assumes.
    public const string TenantSecretsCreate = "tenant.secrets.create";
    public const string TenantSecretsList = "tenant.secrets.list";
    public const string TenantSecretsFetch = "tenant.secrets.fetch";
    public const string TenantSecretsGet = "tenant.secrets.get";
    public const string TenantSecretsUpdate = "tenant.secrets.update";
    public const string TenantSecretsDelete = "tenant.secrets.delete";

    // AdminAuditLogEndpoints: /tenants/{tenantId}/audit-logs
    public const string TenantAuditLogAccess = "tenant.auditLog.access";

    // AdminAgentAccessEndpoints, AdminOwnershipEndpoints (GetOwnership only), AdminTemplateEndpoints —
    // three groups that also came off TenantAdminOrSysAdminOnlyFilter, like the agent-certificates,
    // admin-apikeys, webhooks, integrations, integration-metadata, and secrets actions above. Unlike
    // those six, which were migrated as ordinary delegable actions (opening their TenantAdmin-or-
    // SysAdmin floor to being widened via the matrix was an accepted, intended side effect of
    // migrating), the three below are deliberately NonDelegable: their floor is a hard boundary that
    // must never be loosened by a database edit. Each is one shared action for its whole file rather
    // than one per route — see CapabilityAction.NonDelegable and TenantAgentAccessAccess's own comment.

    // AdminAgentAccessEndpoints: /tenants/{tenantId}/agent-access, /tenants/{tenantId}/agents/{agentId}/access
    // One shared action for the whole surface (not one per route, unlike the rest of this catalog): its
    // TenantAdmin-or-SysAdmin floor must never become runtime-editable, so per-route granularity buys
    // nothing here.
    public const string TenantAgentAccessAccess = "tenant.agentAccess.access";

    // AdminOwnershipEndpoints: /tenants/{tenantId}/agents/{agentId}/ownership
    // Only GetOwnership is migrated. TransferOwnership also accepts the agent's current owner (not a
    // role), which the matrix cannot express, so it keeps its own inline check and has no matching
    // action here.
    public const string TenantOwnershipGet = "tenant.ownership.get";

    // AdminTemplateEndpoints: template browse/get/update/delete/deployment routes
    // One shared action, same reasoning as TenantAgentAccessAccess. Several of these routes
    // additionally self-restrict to SysAdmin-only (or SysAdmin-or-own-tenant) via their own inline
    // check, independent of and unaffected by this floor.
    public const string TenantTemplatesAccess = "tenant.templates.access";

    /// <summary>
    /// The <c>tenant.users.*</c>-shaped defaults ([TenantAdmin]) mark a route that carried no role
    /// check of its own before migration. This does not rely on authentication alone restricting who
    /// reaches AdminApi: <c>AdminRoleTenantResolver</c> (API-key auth) does refuse anyone but
    /// SysAdmin/TenantAdmin, but <c>AdminKeylessUserResolver</c> (ID-token auth) authenticates any
    /// approved tenant member, including <c>TenantUser</c>/<c>TenantParticipant</c>. The actual gate is
    /// <c>CapabilityMatrixFilter</c>'s role check below, against exactly the roles each action lists —
    /// seeding TenantAdmin here reproduces what the pre-migration inline check allowed, nothing more.
    /// An empty default marks a route that was SysAdmin-only, via either a shared filter or a private
    /// inline check removed by this same migration; SysAdmin is never listed, since its access is the
    /// unconditional code-level bypass in <c>CapabilityMatrixFilter</c>.
    /// </summary>
    public static readonly IReadOnlyList<CapabilityAction> All =
    [
        new(TenantUsersList, [SystemRoles.TenantAdmin],
            "List the participant users of a tenant."),
        new(TenantUsersGet, [SystemRoles.TenantAdmin],
            "Read a single participant user of a tenant."),
        new(TenantUsersCreate, [SystemRoles.TenantAdmin],
            "Create a participant user, or grant a tenant role to an existing account."),
        new(TenantUsersUpdate, [SystemRoles.TenantAdmin],
            "Update a tenant participant user's name, email, role, or approval."),
        new(TenantUsersDelete, [SystemRoles.TenantAdmin],
            "Remove a user's membership of a tenant entirely."),
        new(TenantUsersRolesRemove, [SystemRoles.TenantAdmin],
            "Remove one role from a user's tenant membership."),

        new(GlobalUsersList, [],
            "List user accounts across every tenant."),
        new(GlobalUsersGet, [],
            "Read one user account and all of its tenant memberships."),
        new(GlobalUsersUpdate, [],
            "Update a user account's global profile (name, email).",
            NonDelegable: true),
        new(GlobalUsersSysAdminSet, [],
            "Grant or revoke the system administrator flag on an account.",
            NonDelegable: true),
        new(GlobalUsersStatusSet, [],
            "Enable or disable a user account platform-wide.",
            NonDelegable: true),
        new(GlobalUsersDelete, [],
            "Permanently delete a user account.",
            NonDelegable: true),

        new(TenantAgentActivationsList, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List an agent's activation instances in a tenant."),
        new(TenantAgentActivationsGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a single agent activation instance."),
        new(TenantAgentActivationsCreate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Create a new agent activation instance."),
        new(TenantAgentActivationsUpdate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Update an agent activation instance's configuration."),
        new(TenantAgentActivationsActivate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Start the workflow behind an agent activation."),
        new(TenantAgentActivationsDeactivate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Cancel the workflow behind an agent activation."),
        new(TenantAgentActivationsDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete an agent activation instance."),

        new(TenantDataSchema, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Discover available document data types and filters for a tenant."),
        new(TenantDataList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read paginated document data for a tenant."),
        new(TenantDataDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete document data matching a type and filter."),
        new(TenantDataDeleteRecord, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete a single document data record by id."),
        new(TenantDataDeleteByActivation, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete all document data for one agent activation."),

        new(TenantFeedbackSubmit, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Submit a rating for an outgoing agent message."),
        new(TenantFeedbackStats, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read aggregated feedback statistics for a tenant."),
        new(TenantFeedbackList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List feedback entries for a tenant."),
        new(TenantFeedbackGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read one feedback entry with its surrounding thread."),
        new(TenantFeedbackDeleteByActivation, [SystemRoles.TenantAdmin],
            "Delete all feedback for one agent activation."),

        new(TenantHeartbeatCheck, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Check whether an agent activation's workers are responding."),

        new(TenantLogsStreams, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List distinct workflow log streams for a tenant."),
        new(TenantLogsList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Query workflow execution logs for a tenant."),
        new(TenantLogsDeleteByActivation, [SystemRoles.TenantAdmin],
            "Delete all logs for one agent activation."),

        new(TenantMessagingListen, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Stream an agent activation's conversation over SSE."),
        new(TenantMessagingSend, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Send a message to an agent activation."),
        new(TenantMessagingSendFile, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Send a file message to an agent activation."),
        new(TenantMessagingDownloadFile, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Download a file previously sent in a conversation."),
        new(TenantMessagingTopics, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List conversation topics for an agent activation."),
        new(TenantMessagingHistory, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read conversation message history."),
        new(TenantMessagingDeleteByTopic, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete messages in a conversation topic."),
        new(TenantMessagingDeleteByActivation, [SystemRoles.TenantAdmin],
            "Delete all messages for one agent activation."),

        new(TenantMetricsStats, [SystemRoles.TenantAdmin],
            "Read aggregated performance metrics for a tenant."),
        new(TenantMetricsTimeSeries, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read time-series performance metrics for a tenant."),
        new(TenantMetricsCategories, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Discover available metric categories and types."),
        new(TenantMetricsDeleteByActivation, [SystemRoles.TenantAdmin],
            "Delete all performance metrics for one agent activation."),

        new(TenantSchedulesList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List an agent's schedules."),
        new(TenantSchedulesGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a single schedule belonging to an agent."),
        new(TenantSchedulesUpcomingRuns, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a schedule's next upcoming executions."),
        new(TenantSchedulesHistory, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a schedule's execution history."),
        new(TenantSchedulesPause, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Pause a schedule."),
        new(TenantSchedulesResume, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Resume a paused schedule."),
        new(TenantSchedulesDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete a single schedule."),
        new(TenantSchedulesDeleteAll, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete every schedule belonging to an agent."),
        new(TenantSchedulesDeleteByActivation, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete all schedules for one agent activation."),

        new(TenantStatsGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read aggregated tenant statistics."),

        new(TenantTasksList, [SystemRoles.TenantAdmin],
            "List HITL tasks for a tenant."),
        new(TenantTasksGet, [SystemRoles.TenantAdmin],
            "Read a single HITL task."),
        new(TenantTasksUpdateDraft, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Update a HITL task's draft."),
        new(TenantTasksUpdateMetadata, [SystemRoles.TenantAdmin],
            "Merge metadata into a HITL task."),
        new(TenantTasksPerformAction, [SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Perform an action on a HITL task."),

        new(TenantWorkflowsActivate, [SystemRoles.TenantAdmin],
            "Start a new workflow execution."),
        new(TenantWorkflowsGet, [SystemRoles.TenantAdmin],
            "Read a single workflow execution by id."),
        new(TenantWorkflowsList, [SystemRoles.TenantAdmin],
            "List workflow executions."),
        new(TenantWorkflowsEvents, [SystemRoles.TenantAdmin],
            "Read a workflow execution's event history."),
        new(TenantWorkflowsEventsStream, [SystemRoles.TenantAdmin],
            "Stream a workflow execution's event history over SSE."),
        new(TenantWorkflowsTypes, [SystemRoles.TenantAdmin],
            "List the workflow types an agent exposes."),
        new(TenantWorkflowsCancel, [SystemRoles.TenantAdmin],
            "Cancel a running workflow execution."),

        new(TenantAgentDeploymentsList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List an agent's deployments in a tenant."),
        new(TenantAgentDeploymentsGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a single agent deployment."),
        new(TenantAgentDeploymentsUpdate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Update an agent deployment."),
        new(TenantAgentDeploymentsDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete an agent deployment."),
        new(TenantAgentDeploymentsPromoteToTemplate, [],
            "Promote a running tenant-scoped agent deployment into a global template."),

        new(TenantWorkerDeploymentsList, [],
            "List a tenant's Temporal Worker Deployments."),
        new(TenantWorkerDeploymentsGet, [],
            "Describe a single Temporal Worker Deployment."),
        new(TenantWorkerDeploymentsSetCurrentVersion, [],
            "Promote a build to the current version for a Worker Deployment."),
        new(TenantWorkerDeploymentsSetRampingVersion, [],
            "Ramp a percentage of traffic to a build."),

        new(GlobalParticipantsGetByEmail, [],
            "Look up which tenants and roles an email address reaches, across every tenant."),
        new(GlobalParticipantsGetByUserId, [],
            "Look up which tenants and roles a user account reaches, across every tenant."),

        new(TenantsList, [],
            "List every tenant on the platform."),
        new(TenantsGet, [],
            "Read a single tenant by id, outside the caller's own tenant scope."),
        new(TenantsCreate, [],
            "Create a new tenant."),
        new(TenantsUpdate, [],
            "Update a tenant's profile."),
        new(TenantsDelete, [],
            "Delete a tenant.",
            NonDelegable: true),
        new(TenantsMetadataList, [],
            "Read a tenant's metadata, including decrypted secret values.",
            NonDelegable: true),
        new(TenantsMetadataGet, [],
            "Read a single tenant metadata entry by key, decrypted when it is a secret.",
            NonDelegable: true),
        new(TenantsMetadataUpsert, [],
            "Create or replace a tenant metadata entry."),
        new(TenantsMetadataDelete, [],
            "Delete a tenant metadata entry."),


        new(TenantLogoGet, [SystemRoles.TenantAdmin],
            "Read a tenant's logo image."),
        new(TenantLogoSet, [SystemRoles.TenantAdmin],
            "Set or replace a tenant's logo."),
        new(TenantLogoClear, [SystemRoles.TenantAdmin],
            "Remove a tenant's logo."),
        new(TenantThemeGet, [SystemRoles.TenantAdmin],
            "Read a tenant's UI theme."),
        new(TenantThemeSet, [SystemRoles.TenantAdmin],
            "Set a tenant's UI theme."),
        new(TenantThemeClear, [SystemRoles.TenantAdmin],
            "Remove a tenant's UI theme."),

        new(TenantTemporalConfigGet, [],
            "Read a tenant's dedicated Temporal connection override."),
        new(TenantTemporalConfigSet, [],
            "Create or replace a tenant's dedicated Temporal connection override."),
        new(TenantTemporalConfigRevert, [],
            "Revert a tenant to the platform's default Temporal connection."),
        new(TenantTemporalConfigTestConnection, [],
            "Test a Temporal connection without saving it."),

        new(TenantOidcConfigGet, [],
            "Read a tenant's OIDC token-acceptance configuration."),
        new(TenantOidcConfigUpsert, [],
            "Create or replace a tenant's OIDC token-acceptance configuration."),
        new(TenantOidcConfigDelete, [],
            "Delete a tenant's OIDC token-acceptance configuration."),
        new(TenantOidcConfigTemplate, [],
            "Read the example/template shape of a tenant OIDC configuration."),

        new(TenantAgentCertificatesGenerate, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Generate a new agent certificate for a user."),
        new(TenantAgentCertificatesList, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List agent certificates issued to a user."),
        new(TenantAgentCertificatesRevoke, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Revoke an agent certificate."),

        new(TenantAdminApiKeysCreate, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Create a named admin API key for a user."),
        new(TenantAdminApiKeysList, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List admin API keys created by a user."),
        new(TenantAdminApiKeysGet, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a single admin API key by id."),
        new(TenantAdminApiKeysRevoke, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Revoke an admin API key."),
        new(TenantAdminApiKeysRotate, [SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Rotate an admin API key."),

        new(GlobalIntegrationMetadataList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List the supported app integration platform types and their configuration fields."),

        new(TenantWebhooksCreate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Create a builtin webhook integration for a tenant."),
        new(TenantWebhooksList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List builtin webhook integrations for a tenant."),
        new(TenantWebhooksDeleteByActivation, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete all builtin webhooks for one agent activation."),
        new(TenantWebhooksDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete a single builtin webhook integration."),

        new(TenantIntegrationsList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List app integrations for a tenant."),
        new(TenantIntegrationsGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a single app integration by id."),
        new(TenantIntegrationsCreate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Create a new app integration."),
        new(TenantIntegrationsUpdate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Update an existing app integration."),
        new(TenantIntegrationsDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete an app integration."),
        new(TenantIntegrationsEnable, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Enable an app integration."),
        new(TenantIntegrationsDisable, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Disable an app integration."),
        new(TenantIntegrationsTest, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Test an app integration's connectivity."),
        new(TenantIntegrationsWebhookUrl, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read an app integration's webhook URL and setup instructions."),

        new(TenantSecretsCreate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Create a secret in the tenant's secret vault."),
        new(TenantSecretsList, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "List secret keys in the tenant's secret vault."),
        new(TenantSecretsFetch, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Fetch a secret's decrypted value."),
        new(TenantSecretsGet, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Read a secret's metadata without its value."),
        new(TenantSecretsUpdate, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Update a secret's value in the tenant's secret vault."),
        new(TenantSecretsDelete, [SystemRoles.TenantParticipantAdmin, SystemRoles.TenantUser, SystemRoles.TenantAdmin],
            "Delete a secret from the tenant's secret vault."),

        new(TenantAuditLogAccess, [SystemRoles.TenantAdmin],
            "Read the tenant's admin-action audit trail (list entries, performed-by and activation-name filter options)."),

        new(TenantAgentAccessAccess, [SystemRoles.TenantAdmin],
            "Manage per-agent and per-tenant agent access lists.",
            NonDelegable: true),
        new(TenantOwnershipGet, [SystemRoles.TenantAdmin],
            "View an agent's ownership and access information.",
            NonDelegable: true),
        new(TenantTemplatesAccess, [SystemRoles.TenantAdmin],
            "Browse, view, update, delete, or deploy agent templates.",
            NonDelegable: true),
    ];

    private static readonly Dictionary<string, CapabilityAction> ByName =
        All.ToDictionary(action => action.Name, StringComparer.Ordinal);

    public static CapabilityAction? Find(string name) => ByName.GetValueOrDefault(name);
}
