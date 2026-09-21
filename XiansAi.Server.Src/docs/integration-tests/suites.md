# API suites

Tests are grouped by feature slice under [`XiansAi.Server.Tests/IntegrationTests/`](../../../XiansAi.Server.Tests/IntegrationTests/). Names follow `{Surface}{Area}EndpointsTests`. Use these tables to find coverage; open the class for the exact routes and assertions.

## Admin API (Mongo only)

These classes use [`AdminApiIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiIntegrationTestBase.cs) and **do not** start Temporal. Workflow-adjacent routes are covered here only for validation, authz, and Mongo-backed CRUD. Success paths that need a cluster live in [Temporal tests](./temporal.md).

| Class | Coverage |
| --- | --- |
| `AdminAgentEndpointsTests` | List, get, create, update, delete tenant agents |
| `AdminAgentActivationEndpointsTests` | Activation CRUD and Mongo-side activate/deactivate flags |
| `AdminAgentAccessEndpointsTests` | Participant access to an agent |
| `AdminApiKeyEndpointsTests` | Admin API key create/list/revoke |
| `AdminAppIntegrationEndpointsTests` | App integration credentials |
| `AdminAuditLogEndpointsTests` | Audit log query |
| `AdminBootstrapEndpointsTests` | Anonymous platform bootstrap (clears users first) |
| `AdminDataEndpointsTests` | Tenant data export/import-style admin data routes |
| `AdminFeedbackEndpointsTests` | Feedback list and moderation |
| `AdminGlobalUserEndpointsTests` | Cross-tenant user admin |
| `AdminKnowledgeEndpointsTests` | Knowledge CRUD scoped to a tenant |
| `AdminLogsEndpointsTests` | Log query |
| `AdminMessagingEndpointsTests` | Messaging history/send validation and persistence without a worker; GridFS download isolation (no Temporal) |
| `AdminMetricsEndpointsTests` | Usage metrics |
| `AdminOwnershipEndpointsTests` | Agent ownership transfer |
| `AdminParticipantsEndpointsTests` | Participant listing |
| `AdminSecretVaultEndpointsTests` | Tenant secret vault |
| `AdminTemplateEndpointsTests` | System templates (Mongo records, not Lib upload) |
| `AdminTenantEndpointsTests` | Tenant CRUD, config, and isolation |
| `AdminUserEndpointsTests` | Tenant users and roles |
| `AdminTemporalAdjacentEndpointsTests` | Stats/tasks/heartbeat/workflow **validation and authz** with Temporal mocked |
| `WorkflowManagementEndpointsTests` | Missing definition, bad workflow id shape, and similar HTTP failures |

Typical arrange for these tests:

```csharp
var tenantId = $"test-tenant-{Guid.NewGuid()}";
await ConfigureAdminApiClientAsync(tenantId);
await CreateTestTenantAsync(tenantId);
```

## Admin API (Temporal)

See [Temporal tests](./temporal.md). Classes: `AdminApiTemporalEndpointsTests`, `AdminApiTemporalLifecycleTests`, `AdminApiTemporalScheduleAndTaskTests`, `AdminApiTemporalEchoAgentLifecycleTests`, `AdminApiTemporalKnowledgeAgentLifecycleTests`, `AdminApiTemporalSecretVaultAgentLifecycleTests`, `AdminApiTemporalDocumentDbAgentLifecycleTests`, `AdminApiTemporalWebhookAgentLifecycleTests`, `AdminApiTemporalFileMessagingAgentLifecycleTests`, `AdminApiTemporalCustomWorkflowAgentLifecycleTests`, `AdminApiTemporalScheduleAgentLifecycleTests`, `AdminApiTemporalHitlTaskAgentLifecycleTests`, `AdminApiTemporalCrossAgentWorkflowLifecycleTests`, `AdminApiTemporalActivationSdkAgentLifecycleTests`, `AdminApiTemporalMetricsAgentLifecycleTests`.

Lib-backed cycles (Echo chat/SSE, Knowledge overrides, Secret Vault strict scopes, Document DB, builtin Webhooks, file messaging, custom workflows, schedules, HITL tasks, cross-agent workflows, activations SDK, metrics) are documented in [Lib agent workflows](./lib-agent-workflows.md).

## Web API

Derive from [`WebApiIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/WebApi/WebApiIntegrationTestBase.cs). `AuthProvider:Provider=Oidc` must stay set so these routes are mapped.

| Class | Coverage |
| --- | --- |
| `AgentEndpointsTests` | Client agent list/get |
| `ApiKeyEndpointsTests` | User-facing API keys |
| `AuditingEndpointsTests` | Audit queries |
| `KnowledgeEndpointsTests` | Knowledge CRUD |
| `LogsEndpointsTests` | Log queries including skip/empty pages |
| `MessagingEndpointsTests` | Conversations and messages |
| `PermissionsEndpointsTests` | Permission assignment |
| `RoleManagementEndpointsTests` | Tenant roles |
| `TemplateEndpointsTests` | System-scoped template agents (certificate generation is skipped; it needs a real signing PFX) |
| `TenantEndpointsTests` | Current-user tenant list |
| `UserManagementEndpointsTests` | Tenant user list/invite |
| `UserTenantEndpointsTests` | User–tenant membership |
| `WorkflowEndpointsTests` | Workflow listing smoke tests |

## Agent API

These use `IntegrationTestBase` directly (certificate policy is remapped to `Test`). They cover the Lib-facing `/api/agent/*` surface.

| Class | Coverage |
| --- | --- |
| `ActivationEndpointTests` | Exists, list, create, activate, deactivate |
| `ActivityHistoryTests` | Activity history write/read |
| `AgentEndpointsTests` | Agent existence |
| `CacheEndpointTests` | Cache set/get |
| `ConversationEndpointsTests` | Conversation threads and messages |
| `DefinitionsEndpointsTests` | Flow definition upload/get |
| `FileEndpointsTests` | Inbound files |
| `InstructionsEndpointTests` | Instructions |
| `LogsEndpointTests` | Agent logs |
| `OutboundFileTests` | Outbound files |
| `SecretVaultEndpointsTests` | Agent secret vault |

## User API

UserApi `EndpointAuthPolicy` is **not** overridden. Tests create a key in Mongo and call with `apikey`.

| Class | Coverage |
| --- | --- |
| `RestEndpointsTests` | Unauthorized without key; invalid type/body; validation before Temporal |
| `WebhookEndpointsTests` | Inbound user webhooks (auth and validation) |

Live UserApi SSE (`/api/user/sse/events`) and ChatHub (`/ws/chat`) are asserted in the Echo Lib cycle (AdminApiTemporal), not in this Mongo-only class. The builtin webhook success path (`POST /api/user/webhooks/builtin` with `apikeyId`, Integrator `OnWebhook`) is the Webhooks Lib cycle. See [Lib agent workflows](./lib-agent-workflows.md) and [User API docs](../user-api/index.md).

## Apps API

| Class | Coverage |
| --- | --- |
| `AppWebhookEndpointsTests` | Unknown integration id returns 404 without leaking existence |

## Adding a test

1. Pick the matching base class from [Host and fixtures](./host.md).
2. Put the file next to the other tests for that feature (`IntegrationTests/{Feature}Api/`).
3. Seed through existing helpers; do not open `IMongoDatabase` in the test unless there is no helper yet.
4. Assert one status code and the fields that prove the behaviour.
5. Use unique tenant/agent names. Do not reuse `test-tenant` as a write target in Admin tests (the factory already seeded it).
6. If the path needs Temporal **success** behaviour, add it to the `AdminApiTemporal` collection ([Temporal tests](./temporal.md)). If it only needs 400/401/404, keep Temporal mocked (`AdminTemporalAdjacentEndpointsTests` or a new Mongo-only class).
7. Do not add a second test that only changes cosmetic input on the same path.

Skeleton for a Mongo-only Admin test:

```csharp
public class AdminExampleEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminExampleEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetExample_WhenMissing_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/example/missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

## Manual HTTP files

[`XiansAi.Server.Tests/http/`](../../../XiansAi.Server.Tests/http/) contains `.http` requests for exploring Admin, Web, and Agent APIs by hand. They are not executed by `dotnet test`. Copy [`http/.env.example`](../../../XiansAi.Server.Tests/http/.env.example) locally; do not commit real keys.
