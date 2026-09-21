# Lib agent workflows

Most Temporal Admin tests use an in-process [`StubAgentWorkflow`](../../../XiansAi.Server.Tests/TestUtils/StubAgentWorkflow.cs). Lib-backed cycles instead author a real agent with sibling [Xians.Lib](../../../../XiansAi.Lib/Xians.Lib) — the same SDK production agents use — then drive it through Admin HTTP.

Local Temporal setup for the collection is in [Temporal tests](./temporal.md). Shared host: [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs). Shared Admin HTTP: [`AdminApiTemporalLibAgentSupport`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalLibAgentSupport.cs).

| Cycle | Test | What it proves |
| --- | --- | --- |
| Echo | [`AdminApiTemporalEchoAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalEchoAgentLifecycleTests.cs) | Template → deploy → chat, plus live SSE/SignalR |
| Knowledge | [`AdminApiTemporalKnowledgeAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalKnowledgeAgentLifecycleTests.cs) | System knowledge upload, tenant override, activation override, isolation |
| Secret Vault | [`AdminApiTemporalSecretVaultAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalSecretVaultAgentLifecycleTests.cs) | Create/fetch/update/delete through a running agent; strict tenant / agent / participant / activation isolation; Admin never sees values |
| Document DB | [`AdminApiTemporalDocumentDbAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalDocumentDbAgentLifecycleTests.cs) | Agent `SaveAsync` / `GetByKeyAsync`; Admin list/get/update/create; isolation by tenant, agent, activation, and participant |
| Webhooks | [`AdminApiTemporalWebhookAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalWebhookAgentLifecycleTests.cs) | Integrator `OnWebhook` + `context.Respond`; agent SDK create/list; Admin create/list/delete; inbound `POST /api/user/webhooks/builtin` with `apikeyId`; tenant/agent isolation; 401 after revoke; 409 after deactivate |
| Files | [`AdminApiTemporalFileMessagingAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalFileMessagingAgentLifecycleTests.cs) | User `POST .../send/file` → `OnFileUpload` hydrates GridFS bytes; agent `ReplyWithFileAsync` / `SendFileAsync`; history is `fileId` refs only; Admin download tenant isolation |
| Custom workflows | [`AdminApiTemporalCustomWorkflowAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalCustomWorkflowAgentLifecycleTests.cs) | `DefineCustom` + `XiansContext.Workflows` `ExecuteAsync` / `StartAsync` / `SignalAsync`; `Activable=true` Onboarding starts on Admin activate; Admin list/get/types/cancel; uniqueKey IDs; UseExisting on a running Approval; tenant GET isolation |
| Schedules | [`AdminApiTemporalScheduleAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleAgentLifecycleTests.cs) | Activable Setup `CreateIfNotExistsAsync` on Tick; interval fires; Admin list/get/history/pause/resume/delete; tenant list isolation |

```bash
dotnet test --filter "FullyQualifiedName~EchoAgent_TemplateDeployActivateMessageDeactivateAndRemove"
dotnet test --filter "FullyQualifiedName~KnowledgeAgent_SystemUpload_TenantAndActivationOverridesIsolate"
dotnet test --filter "FullyQualifiedName~SecretVaultAgent_CreateFetch_StrictScopeIsolationAndRotation"
dotnet test --filter "FullyQualifiedName~DocumentDbAgent_SavePush_AdminReadModifyAdd_Isolates"
dotnet test --filter "FullyQualifiedName~WebhookAgent_InboundBuiltin_AdminCrudIsolatesAndRevokes"
dotnet test --filter "FullyQualifiedName~FileMessagingAgent_UserUploadAndAgentSend_RoundTripAndIsolate"
dotnet test --filter "FullyQualifiedName~CustomWorkflowAgent_DefineCustom_StartExecuteSignalAndAdminOps"
dotnet test --filter "FullyQualifiedName~SchedulerAgent_ActivableSetup_CreatesScheduleAndAdminOps"
```

The tests project references `../../XiansAi.Lib/Xians.Lib/Xians.Lib.csproj`. Clone that repo next to this one or restore fails for the whole test project.

## Shared host

Every Lib cycle starts the same way so a new agent does not copy loopback / certificate / worker shutdown:

```csharp
await using var host = await LibAgentWorkflowHost.StartAsync(
    _factory.Server, Temporal.TargetHost, Temporal.Namespace, tenantId, _adminUserId!);

var agent = host.RegisterTemplate(agentName, description);
// define workflows / upload knowledge
await host.StartWorkersAsync(agent);
await WaitForTemplateAsync(agentName);
await DeployLibTemplateAsync(tenantId, agentName);
var activationId = await ActivateLibAgentAsync(tenantId, agentName, "front-desk");
```

The host resets Lib statics, binds `TestServerLoopback`, initializes `XiansPlatform` with a PFX key and **knowledge cache disabled** (so Admin overrides are visible on the next chat), and stops workers on dispose.

Register as many templates as the cycle needs, then `StartWorkersAsync(agentA, agentB, …)`. Handlers are keyed by workflow type (`{agent}:Supervisor Workflow`), so two agents on one host do not overwrite each other.

## Why a Lib agent exists in this suite

The stub worker proves Admin routes can start, signal, and cancel Temporal workflows. It does not prove:

- Agent definition upload from the SDK (`RunAllAsync`)
- System template → tenant deploy
- Built-in supervisor chat (`OnUserChatMessage` / `ReplyAsync`)
- Task-queue selection for system-scoped agents
- Activate / send / deactivate / delete against a worker that registered itself
- Knowledge fallback as the running agent actually reads it (`GetAsync` inside the supervisor)
- Secret Vault strict scope as the running agent writes and reads it (`TenantScope()` / `FetchByKeyAsync` inside the supervisor)
- Document DB Type+Key as the running agent writes and reads it (`SaveAsync` / `GetByKeyAsync` inside the supervisor)
- Builtin inbound webhooks as the running Integrator handles them (`OnWebhook` / `context.Respond`, SDK `Webhooks.CreateAsync`)
- File messages both ways: user `POST .../send/file` into `OnFileUpload`, agent `ReplyWithFileAsync` / `SendFileAsync` back through GridFS
- Custom Temporal classes registered with `DefineCustom` and driven through `XiansContext.Workflows`
- Schedules created from an activable workflow (`CreateIfNotExistsAsync`) and managed through Admin HTTP

Echo is the chat/fan-out contract. Knowledge is the scoped-knowledge contract (fallback). Secret Vault is the scoped-secret contract (strict match). Document DB is the agent's persistent JSON store (Type+Key, auto-scoped queries). Webhooks is the inbound Integrator contract (`POST /api/user/webhooks/builtin`). Files is the first-class `File` message contract (bytes in GridFS, `fileId` on the wire). Custom workflows is `DefineCustom` + Start / Execute / Signal plus Admin list/get/types/cancel. Schedules is the [self-scheduling](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/scheduling/) contract (activable Setup creates a Tick interval). None of these is a catalogue of every Lib sample.

## Agent under test: Echo

The test registers the same shape as [`Xians.Examples/EchoAgent`](../../../../XiansAi.Lib/Xians.Examples/EchoAgent/Program.cs):

```csharp
var agent = platform.Agents.Register(new XiansAgentRegistration
{
    Name = agentName,          // unique per run: "Echo {guid}"
    Description = "A simple conversational agent that echoes back user messages",
    SamplePrompts = ["Hello, Echo Agent!", "Echo this message back to me"],
    IsTemplate = true,
    EnableTasks = false
});

var supervisor = agent.Workflows.DefineSupervisor();
supervisor.OnUserChatMessage(async context =>
{
    var userMessage = context.Message.Text ?? string.Empty;
    await context.ReplyAsync($"Echo: {userMessage}");
});

await agent.RunAllAsync(cancellationToken);
```

| Sample EchoAgent | What the test does |
| --- | --- |
| Name `"Echo Agent"` | Unique `"Echo {guid}"` so parallel/repeated runs do not collide |
| `IsTemplate = true` | Same — required for the system queue (see below) |
| `DefineSupervisor()` + echo `ReplyAsync` | Same |
| `DefineIntegrator()` + webhook handler | Omitted — webhooks are not part of this cycle |
| Tasks enabled by default on the platform | `EnableTasks = false` — no HITL task workflow |
| Reads `XIANS_SERVER_URL` / `XIANS_API_KEY` | Loopback URL + generated PFX key |

## Workflows Lib actually starts

`DefineSupervisor()` is `DefineBuiltIn("Supervisor Workflow")`. Lib builds a dynamic Temporal type that extends [`BuiltinWorkflow`](../../../../XiansAi.Lib/Xians.Lib/Temporal/Workflows/BuiltinWorkflow.cs).

| Workflow | Temporal type | Activable | Task queue (system template) | In this test |
| --- | --- | --- | --- | --- |
| Supervisor | `{agentName}:Supervisor Workflow` | `false` | `{agentName}:Supervisor Workflow` | Yes |
| Integrator | `{agentName}:Integrator Workflow` | `false` | system queue of that type | Webhooks cycle only |
| Task (HITL) | `{agentName}:Task Workflow` | n/a | `hitl_task:…` prefix | No (`EnableTasks = false`) |

Built-in workflows are **not activable**. [`ActivationService`](../../Shared/Services/ActivationService.cs) skips `Activable = false` definitions, so **activate does not `StartWorkflow` for Echo**. Chat uses `SignalWithStart` instead.

`RunAllAsync` first uploads every flow definition to Agent API (`WorkflowDefinitionUploader`), then starts a Temporal worker per workflow on that workflow's queue.

`OnUserChatMessage` is valid only on built-in workflows. It registers a handler on `BuiltinWorkflow`. Inbound chat is the Temporal signal `HandleInboundChatOrData` (`Constants.SIGNAL_INBOUND_CHAT_OR_DATA`). The worker dequeues the payload, runs the handler, and `ReplyAsync` posts the reply back to the server so it lands in messaging history.

## System template, deploy, and queues

`IsTemplate = true` on the Lib registration becomes `SystemScoped = true` on the server. A system worker listens on the **unprefixed** queue (workflow type only). That lets one worker serve every tenant that deployed the template.

After Admin **deploy**, the tenant copy is `SystemScoped = false`, but [`AgentRepository.IsSystemAgent`](../../Shared/Repositories/AgentRepository.cs) is still true if a template of the **same name** exists. [`WorkflowSignalService`](../../Shared/Services/WorkflowSignalService.cs) then builds `NewWorkflowOptions` with `systemScoped: true`, so SignalWithStart still targets the system queue.

```text
Lib worker queue     = Echo {guid}:Supervisor Workflow
Admin send queue     = Echo {guid}:Supervisor Workflow     (because IsSystemAgent is true)
Tenant-only queue    = {tenantId}:Echo {guid}:Supervisor Workflow   (unused here)
```

A tenant-only Lib worker (`IsTemplate = false`) never sees those signals. Uploading the same name as a tenant agent after the template also 409s (`A system scoped agent with the same name already exists`).

Workflow id for the chat (Admin messaging uses activation name as the postfix):

```text
{tenantId}:{agentName}:Supervisor Workflow:{activationName}
```

Example: `test-tenant-…:Echo abc…:Supervisor Workflow:front-desk`.

## Lifecycle the test drives

```mermaid
sequenceDiagram
    participant Test
    participant Lib as Xians.Lib
    participant Admin as Admin API
    participant Temporal
    participant AdminSse as Admin SSE
    participant UserSse as UserApi SSE
    participant TenantHub as /ws/tenant/chat
    participant ChatHub as /ws/chat

    Test->>Lib: Register Echo (IsTemplate=true)
    Test->>Lib: RunAllAsync
    Lib->>Admin: Upload Supervisor definition (system scoped)
    Lib->>Temporal: Poll queue "{agent}:Supervisor Workflow"
    Test->>Admin: GET template by-name (wait until 200)
    Test->>Admin: POST deploy by-name to tenant
    Test->>Admin: POST create activation (participantId = admin user)
    Test->>Admin: POST activate
    Note over Admin: Supervisor is Activable=false; no StartWorkflow
    Test->>AdminSse: GET messaging/listen
    Test->>UserSse: GET /api/user/sse/events
    Test->>TenantHub: SubscribeToAgent
    Test->>ChatHub: SubscribeToAgent
    Test->>Admin: POST messaging/send
    Admin->>Temporal: SignalWithStart HandleInboundChatOrData
    Temporal->>Lib: Signal on system queue
    Lib->>Admin: ReplyAsync("Echo: {text}")
    Test->>Admin: GET messaging/history (contains Echo: {text})
    Test->>AdminSse: event Chat with Echo: {text}
    Test->>UserSse: event Chat with Echo: {text}
    Test->>TenantHub: ReceiveChat with Echo: {text}
    Test->>ChatHub: ReceiveChat with Echo: {text}
    Test->>Admin: POST deactivate
    Test->>Admin: DELETE activation, deployment, template
    Test->>Lib: Cancel RunAllAsync
```

Admin HTTP used (all under `/api/v1/admin`):

1. `POST agentTemplates/by-name/{agent}/deploy?tenantId=…`
2. `POST tenants/{tenant}/agentActivations` — `participantId` must be a real user id, not empty (empty is sanitized then rejected as a Temporal user id)
3. `POST tenants/{tenant}/agentActivations/{id}/activate`
4. `GET tenants/{tenant}/messaging/listen` — subscribe **before** send (`HttpCompletionOption.ResponseHeadersRead`)
5. `GET /api/user/sse/events?workflow&participantId&tenantId` — UserApi SSE, same participant
6. SignalR `SubscribeToAgent` on `/ws/tenant/chat` (API-key tenant hub, long polling against TestServer)
7. SignalR `SubscribeToAgent(workflow, participantId, tenantId)` on `/ws/chat`
8. `POST tenants/{tenant}/messaging/send` — `agentName`, `activationName` (`front-desk`), `participantId` (`reader@example.com`), `text`
9. `GET tenants/{tenant}/messaging/history?agentName&activationName&participantId` until the body contains `Echo: {text}`
10. Assert the same `Echo: {text}` on Admin SSE, UserApi SSE, tenant hub `ReceiveChat`, and ChatHub `ReceiveChat`
11. `POST …/deactivate`
12. `DELETE …/agentActivations/{id}`
13. `DELETE tenants/{tenant}/agentDeployments/{agent}?forceDelete=true`
14. `DELETE agentTemplates/by-name/{agent}?cleanActivations=true` (expects 204)

The assertion is the round-trip through Lib: history **and** the live change-stream fan-out (both SSE surfaces and both SignalR hubs), not merely that send returned 200.

## Live SSE and SignalR

Outgoing Echo replies are inserted into `conversation_message`. [`MongoChangeStreamService`](../../Features/UserApi/Services/MongoChangeStreamService.cs) decrypts them and:

- publishes to [`IMessageEventPublisher`](../../Features/UserApi/Services/MessageEventPublisher.cs) (Admin/Web/User SSE)
- sends `ReceiveChat` to SignalR groups on [`ChatHub`](../../Features/UserApi/Websocket/ChatHub.cs) and [`TenantChatHub`](../../Features/UserApi/Websocket/TenantChatHub.cs)

The Echo test opens all four live subscriptions **before** send, via [`LiveEchoStreams`](../../../XiansAi.Server.Tests/TestUtils/LiveEchoStreams.cs):

| Path | Endpoint | How the test connects |
| --- | --- | --- |
| Admin SSE | `GET /api/v1/admin/tenants/{tenant}/messaging/listen` | Separate `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` (the default RetryHttpClient would wait for the stream to end) |
| UserApi SSE | `GET /api/user/sse/events` | Same streaming client pattern; Bearer Admin `sk-Xnai-…` key (`EndpointAuthPolicy` is not stubbed). `workflow` is the full workflow id so `WorkflowIdentifier` matches the current tenant |
| Tenant SignalR | `/ws/tenant/chat` | `HubConnection` + TestServer handler, **long polling**. `SubscribeToAgent(workflowId, tenantId)` joins the tenant group |
| ChatHub | `/ws/chat` | Same long-polling client. `SubscribeToAgent(workflowId, participantId, tenantId)` joins the participant group |

`WebSockets:Enabled` is on in `appsettings.Tests.json`. TestServer does not keep `HttpContext` on SignalR long-poll after the handshake; [`ChatHub`](../../Features/UserApi/Websocket/ChatHub.cs) falls back to the process-wide `ITenantContext` the same way [`TenantChatHub`](../../Features/UserApi/Websocket/TenantChatHub.cs) does.

UserApi auth mutates the Moq `ITenantContext` singleton. The test re-binds tenant, user, and roles after the four connects so Admin send/history still see the Echo tenant.

Helpers wait for SSE `event: connected` and a connected hub **before** `POST …/messaging/send`, so the change stream has subscribers when `ReplyAsync` writes the outgoing Chat.

## Agent under test: Knowledge

Same host as Echo. The supervisor replies with whatever `GetAsync("playbook")` resolves to — the same read path as [`Xians.Examples/KnowledgeAccess`](../../../../XiansAi.Lib/Xians.Examples/KnowledgeAccess/Program.cs).

Resolution is [`GetLatestByNameForTenantAsync`](../../Shared/Services/KnowledgeService.cs): activation → tenant-default → system. Long form: [Knowledge fallback](../KNOWLEDGE_FALLBACK.md).

```text
1. System agent uploads "playbook" = "system original playbook"
2. Deploy to owner tenant and other tenant; also register a second system agent with the same knowledge name
3. Chat on both tenants and the second agent → original
4. Admin override at tenant, then PATCH content → "tenant override playbook"
5. Owner tenant chat → tenant override
   Other tenant chat → original
   Other agent chat → original
6. Admin override at activation "front-desk", PATCH → "activation override playbook"
7. front-desk chat → activation override
   New activation "back-office" of the same agent → tenant override
   Other agent still → original
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET tenants/{tenant}/knowledge/latest?name&agentName[&activationName]`
- `POST tenants/{tenant}/knowledge/{id}/override/tenant`
- `POST tenants/{tenant}/knowledge/{id}/override/activation?activationName=…`
- `PATCH tenants/{tenant}/knowledge/{id}` (new version; latest is by `CreatedAt`)

Each chat uses a unique `participantId` so history from an earlier step cannot satisfy a later assertion.

## Agent under test: Secret Vault

Same host as Echo and Knowledge. The supervisor parses a short chat command and calls the documented SDK (`TenantScope()` then optional `AgentScope()` / `ParticipantScope()` / `ActivationScope()`). Fetch is **strict** — the read scope must equal the write scope. There is no Knowledge-style fallback. Product behaviour: [Secret Vault](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/secret-vault/).

```text
1. Create tenant-only secret via chat → owner fetch returns the value
   Other tenant fetch of the same key → missing
   Same tenant fetch with AgentScope() → missing (strict)
   Admin list/fetch include the key and never the value
2. Admin creates a second tenant secret (response redacted) → agent TenantScope fetch returns the value
3. Create agent-scoped secret → that agent fetches it; the second agent and TenantScope() do not
4. Create activation-scoped secret on front-desk → front-desk fetches it; back-office and the second agent do not
5. Create participant-scoped secret as owner@… → that participant fetches it; another participant does not
6. Update then delete the tenant secret through the agent → fetch sees rotated value, then missing
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `POST /api/v1/admin/secrets` (value is stripped from the response)
- `GET /api/v1/admin/secrets?tenantId=…` (list metadata, no values)
- `GET /api/v1/admin/secrets/fetch?key&tenantId` (metadata probe, no values)

Values are proven only through the agent's `FetchByKeyAsync` + `ReplyAsync`. Participant isolation reuses a fixed `participantId` for create and fetch; other chats still use a unique id so history cannot collide. History polling matches each message's `text` field, not the raw JSON (a reply of `created` must not match `createdAt`).

## Agent under test: Document DB

Same host as Echo, Knowledge, and Secret Vault. The supervisor saves and reads JSON through `XiansContext.CurrentAgent.Documents` — Type + Key, the same model as [Document DB](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/document-db/). Saves from chat stamp agent, activation, and participant. Gets from chat auto-filter those fields, so another tenant, agent, activation, or participant does not see the owner's document.

```text
1. Agent SaveAsync user-profile / user-{id} { plan: gold } → Admin list/schema/get return gold
2. Admin PUT content { plan: platinum } → agent GetByKeyAsync returns platinum
3. Other tenant, other agent, other participant, other activation → missing
4. Admin POST a second key (bronze) with the same activation and participant → agent GetByKeyAsync returns bronze
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/data/schema?startDate&endDate&agentName`
- `GET /api/v1/admin/tenants/{tenant}/data?startDate&endDate&agentName&dataType`
- `GET /api/v1/admin/tenants/{tenant}/data/{recordId}`
- `PUT /api/v1/admin/tenants/{tenant}/data/{recordId}`
- `POST /api/v1/admin/tenants/{tenant}/data`

Owner save/get/admin-create reuse a fixed `participantId` so auto-scoped queries match. Isolation chats still use a unique id.

## Agent under test: Webhooks

Same host as the other Lib cycles. The agent registers **both** `DefineSupervisor` (SDK create/list via chat) and `DefineIntegrator` (`OnWebhook` + `context.Respond`) — the shape of [`Xians.Examples/EchoAgent`](../../../../XiansAi.Lib/Xians.Examples/EchoAgent/Program.cs) plus [`WebhookCollection`](../../../../XiansAi.Lib/Xians.Lib/Agents/Webhooks/WebhookCollection.cs). Product behaviour: [Webhooks](../WEBHOOKS.md).

Admin `POST /tenants/{tenant}/webhooks` (and Agent `POST /api/agent/webhooks`) creates a webhook-type API key and a `builtin_webhook` integration whose URL is:

```text
/api/user/webhooks/builtin?apikeyId=…&timeoutSeconds=30&agentName=…&workflowName=Integrator Workflow&webhookName=…&activationName=…
```

`apikeyId` is itself the credential. UserApi `EndpointAuthPolicy` is **not** stubbed, so the test posts with a fresh factory client (no Admin Bearer). `EndpointAuthenticationHandler` authenticates `apikeyId` **before** the Authorization header. Inbound headers are forwarded as `WebhookContext.Metadata` (builtin path only). The HTTP caller waits on `IPendingRequestService` until the Integrator handler calls `context.Respond`.

```text
1. Agent Webhooks.CreateAsync EmailReceived via supervisor chat → Admin list returns the URL
2. POST that URL with JSON body + X-Test-Trace → 200 JSON from OnWebhook (agent, tenant, webhook name, payload, header)
3. Other tenant Admin list does not include the owner's id
   Admin creates the same webhook name on the other tenant → POST returns the other tenant id
4. Admin creates the same webhook name on a second agent → POST returns the other agent name
5. POST builtin with no apikeyId → 401
6. Admin creates InvoicePaid on the owner agent → POST returns InvoicePaid; chat list is 2
7. Admin delete EmailReceived (revokes the API key) → POST that URL → 401; chat list is 1
8. Deactivate the owner activation → POST InvoicePaid → 409
```

Admin / UserApi HTTP used beyond the shared deploy/activate helpers:

- `POST /api/v1/admin/tenants/{tenant}/webhooks`
- `GET /api/v1/admin/tenants/{tenant}/webhooks?agentName=…`
- `DELETE /api/v1/admin/tenants/{tenant}/webhooks/{id}`
- `POST /api/user/webhooks/builtin?apikeyId&…` (no Admin Bearer; UserApi authenticates the webhook key)

UserApi auth mutates the Moq `ITenantContext` singleton. Re-bind the owner tenant after inbound POSTs before the next Admin call (`TenantRouteScopeFilter` requires route tenant == context tenant).

Default inbound `participantId` is `webhook`. Workflow id is `{tenant}:{agent}:Integrator Workflow:{activation}`. The Integrator worker listens on the unprefixed system queue, same reason as Supervisor.

## Agent under test: Files

Same host as the other Lib cycles. The supervisor registers `OnFileUpload` and `OnUserChatMessage`, the receive/send shape of [File messaging](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-fileupload/) and [`Xians.Examples/FileUpload`](../../../../XiansAi.Lib/Xians.Examples/FileUpload/Program.cs) (handler path only — not the custom workflow send).

Bytes never ride Temporal signals. Admin `POST /tenants/{tenant}/messaging/send/file` writes GridFS and signals `{ files: [{ fileId, fileName, contentType, fileSize }] }`. The worker downloads those bytes so `context.Message.Files` is populated. `ReplyWithFileAsync` / `SendFileAsync` upload then post an outbound File message with references only.

```text
1. Admin POST send/file invoice-{id}.txt (base64) → 200
2. OnFileUpload reads the hydrated bytes and ReplyWithFileAsync echo-{name} with caption "received {name} {n}"
3. History incoming File has invoice fileId and no content; outgoing File has echo fileId and no content
   Admin GET .../messaging/files/{id} returns the original bytes for both
4. Chat "generate {marker}" → SendFileAsync report.txt → download equals the marker bytes
5. Other tenant GET of the owner's fileId → 404
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `POST /api/v1/admin/tenants/{tenant}/messaging/send/file`
- `GET /api/v1/admin/tenants/{tenant}/messaging/history?agentName&activationName&participantId` (File messages included; `chatOnly` defaults to false)
- `GET /api/v1/admin/tenants/{tenant}/messaging/files/{fileId}`

Reuse a fixed `participantId` for upload, generate, and download so GridFS participant ownership matches the outbound send. The generate chat uses `AssertAgentRepliesWithAsync` on the File message caption.

## Agent under test: Custom workflows

Same host as the other Lib cycles. The supervisor registers **Onboarding** with `Activable = true` and three `DefineCustom` types with `Activable = false`, each with a runtime `typeName` of `{agentName}:…` — the [Workflows](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/workflows/) pattern (`StartAsync`, `ExecuteAsync`, `SignalAsync`, uniqueKey IDs). Admin activate starts only Onboarding (`ActivationService` skips `Activable = false`). Chat handlers run as Temporal activities, so `XiansContext.Workflows` uses the Temporal **client** path (not child-workflow start). Client `StartAsync` sets `IdConflictPolicy = UseExisting`: a second start of a still-running workflow succeeds without creating another execution (it does not throw `WorkflowAlreadyStartedException`). The Approval signal is registered as `ApproveAsync` (Temporal otherwise trims the `Async` suffix from the method name). Template deploy copies `Activable` onto the tenant flow definition so activate can see it.

```text
1. Admin activate → starts only Onboarding at {tenant}:{agent}:Onboarding:front-desk (Running; worker took the task). System and tenant flow copies of the same type may both be activable; the started workflow id is still that one Onboarding id.
2. Chat "check {sku}" → ExecuteAsync Inventory Check → reply in-stock:{sku}
3. Chat "pay {orderId}" → StartAsync Payment uniqueKey=orderId
   Admin list/get {tenant}:{agent}:Payment:front-desk:{orderId} → Completed
4. Chat "hold" → StartAsync Approval (no uniqueKey)
   Admin get {tenant}:{agent}:Approval:front-desk → Running
5. Chat "hold" again → still one Running Approval (UseExisting)
6. GET .../workflows/types includes Onboarding, Inventory Check, Payment, Approval
7. Chat "approve granted" → SignalAsync("ApproveAsync") → Completed
8. Chat "hold" after complete → new run; Admin cancel force=true → Terminated
9. Other tenant GET of the owner's workflowId → 404
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/workflows/list?agent&status` (count matching `workflowId`; TQL rejects `:` in `workflowType`)
- `GET /api/v1/admin/tenants/{tenant}/workflows?workflowId=…`
- `GET /api/v1/admin/tenants/{tenant}/workflows/types?agent=…`
- `POST /api/v1/admin/tenants/{tenant}/workflows/cancel?workflowId=…&force=true`

Custom workers listen on the unprefixed system queue (`{agent}:Onboarding`, `{agent}:Inventory Check`, `{agent}:Payment`, `{agent}:Approval`), same reason as Supervisor.

## Agent under test: Schedules

Same host as the other Lib cycles. Setup is `Activable = true` and Tick is not — the [Scheduling](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/scheduling/) pattern: create the schedule **inside** the activable workflow so the id is `{tenant}:{agent}:{activation}:{scheduleName}`. Admin activate starts Setup; Setup calls `CreateIfNotExistsAsync` on Tick with `.EverySeconds(1)`.

```text
1. Admin activate → starts Setup; Setup creates schedule tick
2. Admin list/get {tenant}:{agent}:front-desk:tick; workflowType is {agent}:Tick
3. GET upcoming-runs; GET history until at least two Tick actions
4. POST pause → status Suspended; executionCount stays put
5. POST resume → status Running
6. Other tenant list does not include the owner's schedule id
7. DELETE by-id → GET by-id 404
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/agents/{agent}/schedules`
- `GET .../schedules/by-id?scheduleId=…`
- `GET .../schedules/upcoming-runs?scheduleId=…`
- `GET .../schedules/history?scheduleId=…`
- `POST .../schedules/pause?scheduleId=…`
- `POST .../schedules/resume?scheduleId=…`
- `DELETE .../schedules/by-id?scheduleId=…`

Stub schedule create/pause/resume/delete (no Lib agent) remains [`AdminApiTemporalScheduleAndTaskTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleAndTaskTests.cs).

## Test harness around Lib

Lib's HTTP client uses `SocketsHttpHandler`. It cannot be given `TestServer.CreateHandler()`. [`TestServerLoopback`](../../../XiansAi.Server.Tests/TestUtils/TestServerLoopback.cs) binds `HttpListener` on `127.0.0.1:{ephemeral}` and forwards to the in-process TestServer. [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) owns that loopback.

Lib API keys are a base64 PFX whose subject is `CN={user}, OU={user}, O={tenant}`. [`XiansLibTestCertificate`](../../../XiansAi.Server.Tests/TestUtils/XiansLibTestCertificate.cs) builds that key. Certificate policies on the host are remapped to `TestAuthHandler` (see [Host and fixtures](./host.md)).

`ITenantContext` is a Moq singleton. `BindTenantContext` assigns `TenantId`, `LoggedInUser`, `ParticipantId`, and roles so Lib uploads and `ReplyAsync` see the same tenant as Admin HTTP.

Lib keeps process-wide statics (handlers, definition-upload cache). The host calls `TestCleanup.ResetAllStaticState()` and `WorkflowDefinitionUploader.ResetCache()` on start and dispose, and cancels `RunAllAsync`.

`XiansPlatform.InitializeAsync` is given the loopback URL, the PFX key, `EnableTasks = false`, `Cache.Enabled = false`, and `TemporalConfiguration` pointing at `TemporalFixture` (same local CLI as the rest of the collection).

## What these cycles do not cover

- HITL task workflows
- File send from workflow code (`XiansContext.Messaging.SendFileAsSupervisorAsync`)
- `SignalWithStartAsync` and typed `GetWorkflowHandleAsync` queries
- Cross-agent `activationName` targeting
- Tenant-scoped agents that are not system templates
- Other Lib samples (`CustomWorkflow` HITL/MAF, …)
- Legacy Temporal Update webhooks (`POST /api/user/webhooks/{workflow}/{methodName}`)

Keep those as separate tests on `LibAgentWorkflowHost` if they become required. Do not grow Echo, Knowledge, Secret Vault, Document DB, Webhooks, Files, Custom workflows, or Schedules into a second sample.

## Adding another Lib agent workflow

Reuse [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) and the helpers on `AdminApiTemporalIntegrationTestBase`:

1. Stay in the `AdminApiTemporal` collection and `AdminApiTemporalIntegrationTestBase`.
2. `await using var host = await LibAgentWorkflowHost.StartAsync(...)`; `BindTenantContext`.
3. `host.RegisterTemplate` with a unique name. Use `IsTemplate = true` if Admin send should hit the system queue.
4. Define only the workflows (and knowledge / secrets / documents / webhooks / files / custom types / schedules) the assertion needs.
5. `StartWorkersAsync` then `WaitForTemplateAsync` before deploy.
6. Drive the public Admin API; poll history or list endpoints instead of a single Temporal visibility read.
7. Dispose of the host (cancels workers and resets Lib statics).

If the new agent is tenant-scoped only (`IsTemplate = false`) and no system template of that name exists, the worker queue is `{tenantId}:{workflowType}` and SignalWithStart must use the same. Do not mix a system template of the same name with a tenant worker.
