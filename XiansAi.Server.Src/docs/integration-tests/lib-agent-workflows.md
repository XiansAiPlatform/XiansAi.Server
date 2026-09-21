# Lib agent workflows

Most Temporal Admin tests use an in-process [`StubAgentWorkflow`](../../../XiansAi.Server.Tests/TestUtils/StubAgentWorkflow.cs). One test instead authors a real agent with sibling [Xians.Lib](../../../../XiansAi.Lib/Xians.Lib) — the same SDK production agents use — then drives that agent through Admin HTTP.

That test is [`AdminApiTemporalEchoAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalEchoAgentLifecycleTests.cs). Local Temporal setup for the collection is in [Temporal tests](./temporal.md).

## Why a Lib agent exists in this suite

The stub worker proves Admin routes can start, signal, and cancel Temporal workflows. It does not prove:

- Agent definition upload from the SDK (`RunAllAsync`)
- System template → tenant deploy
- Built-in supervisor chat (`OnUserChatMessage` / `ReplyAsync`)
- Task-queue selection for system-scoped agents
- Activate / send / deactivate / delete against a worker that registered itself

The Echo cycle is the contract test for that path. It is intentionally small: one built-in supervisor that echoes chat. It is not a catalogue of every Lib sample.

```bash
dotnet test --filter "FullyQualifiedName~EchoAgent_TemplateDeployActivateMessageDeactivateAndRemove"
```

The tests project references `../../XiansAi.Lib/Xians.Lib/Xians.Lib.csproj`. Clone that repo next to this one or restore fails for the whole test project.

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

    Test->>Lib: Register Echo (IsTemplate=true)
    Test->>Lib: RunAllAsync
    Lib->>Admin: Upload Supervisor definition (system scoped)
    Lib->>Temporal: Poll queue "{agent}:Supervisor Workflow"
    Test->>Admin: GET template by-name (wait until 200)
    Test->>Admin: POST deploy by-name to tenant
    Test->>Admin: POST create activation (participantId = admin user)
    Test->>Admin: POST activate
    Note over Admin: Supervisor is Activable=false; no StartWorkflow
    Test->>Admin: POST messaging/send
    Admin->>Temporal: SignalWithStart HandleInboundChatOrData
    Temporal->>Lib: Signal on system queue
    Lib->>Admin: ReplyAsync("Echo: {text}")
    Test->>Admin: GET messaging/history (contains Echo: {text})
    Test->>Admin: POST deactivate
    Test->>Admin: DELETE activation, deployment, template
    Test->>Lib: Cancel RunAllAsync
```

Admin HTTP used (all under `/api/v1/admin`):

1. `POST agentTemplates/by-name/{agent}/deploy?tenantId=…`
2. `POST tenants/{tenant}/agentActivations` — `participantId` must be a real user id, not empty (empty is sanitized then rejected as a Temporal user id)
3. `POST tenants/{tenant}/agentActivations/{id}/activate`
4. `POST tenants/{tenant}/messaging/send` — `agentName`, `activationName` (`front-desk`), `participantId` (`reader@example.com`), `text`
5. `GET tenants/{tenant}/messaging/history?agentName&activationName&participantId` until the body contains `Echo: {text}`
6. `POST …/deactivate`
7. `DELETE …/agentActivations/{id}`
8. `DELETE tenants/{tenant}/agentDeployments/{agent}?forceDelete=true`
9. `DELETE agentTemplates/by-name/{agent}?cleanActivations=true` (expects 204)

The assertion is the round-trip through Lib, not merely that send returned 200.

## Test harness around Lib

Lib's HTTP client uses `SocketsHttpHandler`. It cannot be given `TestServer.CreateHandler()`. [`TestServerLoopback`](../../../XiansAi.Server.Tests/TestUtils/TestServerLoopback.cs) binds `HttpListener` on `127.0.0.1:{ephemeral}` and forwards to the in-process TestServer.

Lib API keys are a base64 PFX whose subject is `CN={user}, OU={user}, O={tenant}`. [`XiansLibTestCertificate`](../../../XiansAi.Server.Tests/TestUtils/XiansLibTestCertificate.cs) builds that key. Certificate policies on the host are remapped to `TestAuthHandler` (see [Host and fixtures](./host.md)).

`ITenantContext` is a Moq singleton. The test assigns `TenantId`, `LoggedInUser`, `ParticipantId`, and roles so Lib uploads and `ReplyAsync` see the same tenant as Admin HTTP.

Lib keeps process-wide statics (handlers, definition-upload cache). The test calls `TestCleanup.ResetAllStaticState()` and `WorkflowDefinitionUploader.ResetCache()` before start and in `finally`, then cancels `RunAllAsync`.

`XiansPlatform.InitializeAsync` is given the loopback URL, the PFX key, `EnableTasks = false`, and `TemporalConfiguration` pointing at `TemporalFixture` (same local CLI as the rest of the collection).

## What this test does not cover

- Integrator / webhook workflows
- HITL task workflows
- Custom (non-built-in) workflow classes
- Tenant-scoped agents that are not system templates
- Live SSE / SignalR of the Echo replies
- Other Lib samples (`FileUpload`, `KnowledgeAccess`, `CustomWorkflow`, …)

Keep those as separate tests if they become required. Do not grow Echo into a second sample.

## Adding another Lib agent workflow

Reuse the Echo harness rather than inventing a second loopback or certificate helper:

1. Stay in the `AdminApiTemporal` collection and `AdminApiTemporalIntegrationTestBase`.
2. Reset Lib statics; start `TestServerLoopback`; bind `ITenantContext`.
3. Register with a unique name. Use `IsTemplate = true` if Admin send should hit the system queue.
4. Define only the workflows the assertion needs.
5. Wait for template upload before deploy.
6. Drive the public Admin API; poll history or list endpoints instead of a single Temporal visibility read.
7. Cancel the worker and reset statics in `finally`.

If the new agent is tenant-scoped only (`IsTemplate = false`) and no system template of that name exists, the worker queue is `{tenantId}:{workflowType}` and SignalWithStart must use the same. Do not mix a system template of the same name with a tenant worker.
