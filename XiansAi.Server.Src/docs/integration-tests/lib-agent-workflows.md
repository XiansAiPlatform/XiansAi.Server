# Lib agent workflows

Most Temporal Admin tests use an in-process [`StubAgentWorkflow`](../../../XiansAi.Server.Tests/TestUtils/StubAgentWorkflow.cs). Lib-backed cycles instead author a real agent with sibling [Xians.Lib](../../../../XiansAi.Lib/Xians.Lib) — the same SDK production agents use — then drive it through Admin HTTP.

Local Temporal setup for the collection is in [Temporal tests](./temporal.md). Shared host: [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs). Shared Admin HTTP: [`AdminApiTemporalLibAgentSupport`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalLibAgentSupport.cs).

| Cycle | Test | What it proves |
| --- | --- | --- |
| Echo | [`AdminApiTemporalEchoAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalEchoAgentLifecycleTests.cs) | Template → deploy → chat, plus live SSE/SignalR |
| Knowledge | [`AdminApiTemporalKnowledgeAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalKnowledgeAgentLifecycleTests.cs) | System knowledge upload, tenant override, activation override, isolation |

```bash
dotnet test --filter "FullyQualifiedName~EchoAgent_TemplateDeployActivateMessageDeactivateAndRemove"
dotnet test --filter "FullyQualifiedName~KnowledgeAgent_SystemUpload_TenantAndActivationOverridesIsolate"
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

Echo is the chat/fan-out contract. Knowledge is the scoped-knowledge contract. Neither is a catalogue of every Lib sample.

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
| Integrator | `{agentName}:Integrator Workflow` | `false` | system queue of that type | No (not registered) |
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

## Test harness around Lib

Lib's HTTP client uses `SocketsHttpHandler`. It cannot be given `TestServer.CreateHandler()`. [`TestServerLoopback`](../../../XiansAi.Server.Tests/TestUtils/TestServerLoopback.cs) binds `HttpListener` on `127.0.0.1:{ephemeral}` and forwards to the in-process TestServer. [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) owns that loopback.

Lib API keys are a base64 PFX whose subject is `CN={user}, OU={user}, O={tenant}`. [`XiansLibTestCertificate`](../../../XiansAi.Server.Tests/TestUtils/XiansLibTestCertificate.cs) builds that key. Certificate policies on the host are remapped to `TestAuthHandler` (see [Host and fixtures](./host.md)).

`ITenantContext` is a Moq singleton. `BindTenantContext` assigns `TenantId`, `LoggedInUser`, `ParticipantId`, and roles so Lib uploads and `ReplyAsync` see the same tenant as Admin HTTP.

Lib keeps process-wide statics (handlers, definition-upload cache). The host calls `TestCleanup.ResetAllStaticState()` and `WorkflowDefinitionUploader.ResetCache()` on start and dispose, and cancels `RunAllAsync`.

`XiansPlatform.InitializeAsync` is given the loopback URL, the PFX key, `EnableTasks = false`, `Cache.Enabled = false`, and `TemporalConfiguration` pointing at `TemporalFixture` (same local CLI as the rest of the collection).

## What these cycles do not cover

- Integrator / webhook workflows
- HITL task workflows
- Custom (non-built-in) workflow classes
- Tenant-scoped agents that are not system templates
- Other Lib samples (`FileUpload`, `CustomWorkflow`, …)

Keep those as separate tests on `LibAgentWorkflowHost` if they become required. Do not grow Echo or Knowledge into a second sample.

## Adding another Lib agent workflow

Reuse [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) and the helpers on `AdminApiTemporalIntegrationTestBase`:

1. Stay in the `AdminApiTemporal` collection and `AdminApiTemporalIntegrationTestBase`.
2. `await using var host = await LibAgentWorkflowHost.StartAsync(...)`; `BindTenantContext`.
3. `host.RegisterTemplate` with a unique name. Use `IsTemplate = true` if Admin send should hit the system queue.
4. Define only the workflows (and knowledge) the assertion needs.
5. `StartWorkersAsync` then `WaitForTemplateAsync` before deploy.
6. Drive the public Admin API; poll history or list endpoints instead of a single Temporal visibility read.
7. Dispose of the host (cancels workers and resets Lib statics).

If the new agent is tenant-scoped only (`IsTemplate = false`) and no system template of that name exists, the worker queue is `{tenantId}:{workflowType}` and SignalWithStart must use the same. Do not mix a system template of the same name with a tenant worker.
