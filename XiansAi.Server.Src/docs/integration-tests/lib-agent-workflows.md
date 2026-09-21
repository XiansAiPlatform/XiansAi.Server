# Lib agent workflows

Most Temporal Admin tests use an in-process [`StubAgentWorkflow`](../../../XiansAi.Server.Tests/TestUtils/StubAgentWorkflow.cs). Lib-backed cycles instead author a real agent with sibling [Xians.Lib](../../../../XiansAi.Lib/Xians.Lib) — the same SDK production agents use — then drive it through Admin HTTP.

Local Temporal setup for the collection is in [Temporal tests](./temporal.md). Shared host: [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs). Shared Admin HTTP: [`AdminApiTemporalLibAgentSupport`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalLibAgentSupport.cs).

| Cycle | Test | What it proves |
| --- | --- | --- |
| Echo | [`AdminApiTemporalEchoAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalEchoAgentLifecycleTests.cs) | Template → deploy → chat, plus live SSE/SignalR |
| Knowledge | [`AdminApiTemporalKnowledgeAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalKnowledgeAgentLifecycleTests.cs) | System knowledge upload, tenant override, activation override, isolation |
| Knowledge list | [`AdminApiTemporalKnowledgeListAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalKnowledgeListAgentLifecycleTests.cs) | `ListAsync` returns tenant knowledge for this agent only |
| Secret Vault | [`AdminApiTemporalSecretVaultAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalSecretVaultAgentLifecycleTests.cs) | Create/fetch/update/delete through a running agent; strict tenant / agent / participant / activation isolation; Admin never sees values |
| Secret Vault SDK | [`AdminApiTemporalSecretVaultSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalSecretVaultSdkAgentLifecycleTests.cs) | TenantScope Create/Fetch/GetById/Update/List/Delete from a Temporal **activity**; List/Delete from **workflow** code (system `SecretVaultActivities` stub); Create/Fetch/GetById/Update refused in a workflow so plaintext never enters history |
| Document DB | [`AdminApiTemporalDocumentDbAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalDocumentDbAgentLifecycleTests.cs) | Agent `SaveAsync` / `GetByKeyAsync`; Admin list/get/update/create; isolation by tenant, agent, activation, and participant |
| Document DB SDK | [`AdminApiTemporalDocumentDbSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalDocumentDbSdkAgentLifecycleTests.cs) | `QueryAsync`, `GetAsync(id)`, `UpdateAsync`, `ExistsAsync`, `DeleteAsync` / `DeleteManyAsync`; Query auto-scope hides another participant |
| Webhooks | [`AdminApiTemporalWebhookAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalWebhookAgentLifecycleTests.cs) | Integrator `OnWebhook` + `context.Respond`; agent SDK create/list; Admin create/list/delete; inbound `POST /api/user/webhooks/builtin` with `apikeyId`; tenant/agent isolation; 401 after revoke; 409 after deactivate |
| Webhook SDK | [`AdminApiTemporalWebhookSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalWebhookSdkAgentLifecycleTests.cs) | SDK `DeleteAsync` revokes the key; Integrator `WebhookResponse.NotFound` is HTTP 404 |
| Files | [`AdminApiTemporalFileMessagingAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalFileMessagingAgentLifecycleTests.cs) | User `POST .../send/file` → `OnFileUpload` hydrates GridFS bytes; agent `ReplyWithFileAsync` / `SendFileAsync`; history is `fileId` refs only; Admin download tenant isolation |
| Workflow files | [`AdminApiTemporalWorkflowFileMessagingAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalWorkflowFileMessagingAgentLifecycleTests.cs) | Custom workflow `XiansContext.Messaging.SendFileAsSupervisorAsync`; GridFS + history `fileId` refs |
| Custom workflows | [`AdminApiTemporalCustomWorkflowAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalCustomWorkflowAgentLifecycleTests.cs) | `DefineCustom` + `XiansContext.Workflows` `ExecuteAsync` / `StartAsync` / `SignalAsync`; `Activable=true` Onboarding starts on Admin activate; Admin list/get/types/cancel; uniqueKey IDs; UseExisting on a running Approval; tenant GET isolation |
| Workflow handle | [`AdminApiTemporalWorkflowHandleAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalWorkflowHandleAgentLifecycleTests.cs) | Client-only `SignalWithStartAsync`; typed `GetWorkflowHandleAsync` + `QueryAsync` + `SignalAsync`; second SignalWithStart hits the running execution |
| Schedules | [`AdminApiTemporalScheduleAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleAgentLifecycleTests.cs) | Activable Setup `CreateIfNotExistsAsync` on Tick; interval fires; Admin list/get/history/pause/resume/delete; tenant list isolation |
| Schedule SDK | [`AdminApiTemporalScheduleSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleSdkAgentLifecycleTests.cs) | `ScheduleCollection` CreateIfNotExists/Exists/List/Get/Pause/Unpause/Trigger/Delete from a Temporal **activity** and from **workflow** code (system `ScheduleActivities` stub); Admin history confirms Trigger; GET 404 after Delete |
| Schedule create | [`AdminApiTemporalScheduleCreateAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleCreateAgentLifecycleTests.cs) | Strict `CreateAsync` throws if it exists; activity-only `DescribeAsync` (paused); Admin GET 404 after Delete |
| HITL tasks | [`AdminApiTemporalHitlTaskAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalHitlTaskAgentLifecycleTests.cs) | `EnableTasks` Review `StartTaskAsync` / `GetResultAsync`; Admin list/get/draft/metadata/action; timeout completes without an action; tenant GET isolation |
| HITL SDK | [`AdminApiTemporalHitlTaskSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalHitlTaskSdkAgentLifecycleTests.cs) | `TaskCollection` UpdateDraft/UpdateMetadata/PerformAction from a Temporal **activity** and from **workflow** code (system `TaskActivities` stub); StartTask/GetResult stay in the parent workflow; result FinalWork/action/metadata |
| HITL conversation | [`AdminApiTemporalHitlTaskConversationAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalHitlTaskConversationAgentLifecycleTests.cs) | `CreateAndWaitAsync`; fire-and-forget `CreateAsync` (`SurviveParentClose`); `HitlTask.FromWorkflowIdAsync` / `ApproveAsync` |
| HITL last task | [`AdminApiTemporalHitlLastTaskIdAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalHitlLastTaskIdAgentLifecycleTests.cs) | Stamp HITL workflow id on chat; `GetLastTaskIdAsync` returns it for that participant only |
| Cross-agent | [`AdminApiTemporalCrossAgentWorkflowLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalCrossAgentWorkflowLifecycleTests.cs) | Invoice `ExecuteAsync` / `StartAsync` / `SignalAsync` on Fraud type strings; no inherited activation postfix; explicit `activationName`; not-found / deactivated; tenant GET isolation |
| Activations SDK | [`AdminApiTemporalActivationSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalActivationSdkAgentLifecycleTests.cs) | Manager `Tenant.Agent(target)` Exists/Create/Activate/status/list/Deactivate from a Temporal **activity** and from **workflow** code (system `ActivationActivities` stub); Target Heartbeat starts on SDK activate; self `ActivationExistsAsync`; tenant isolation |
| Metrics | [`AdminApiTemporalMetricsAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalMetricsAgentLifecycleTests.cs) | `context.Metrics` from chat and `XiansContext.Metrics` from a workflow; Admin stats/categories; `ForModel` filter; tenant/agent isolation; delete by activation |
| Logging | [`AdminApiTemporalLoggingAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalLoggingAgentLifecycleTests.cs) | Activity `XiansLogger.GetLogger` and workflow `Workflow.Logger`; Admin streams/logs; `logLevel` filter; tenant/agent isolation; delete by activation |
| Messaging SDK | [`AdminApiTemporalMessagingSdkAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalMessagingSdkAgentLifecycleTests.cs) | `OnUserDataMessage` / `SendDataAsync`; handler `SendReasoningAsync` / `SendToolExecAsync` + Admin SSE; workflow `SendChatAsSupervisorAsync`; `GetChatHistoryAsync` topic isolation |
| Tenant-scoped | [`AdminApiTemporalTenantScopedAgentLifecycleTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalTenantScopedAgentLifecycleTests.cs) | `IsTemplate = false` (no deploy); worker on `{tenantId}:{workflowType}`; Admin chat round-trip; template GET 404 |

```bash
dotnet test --filter "FullyQualifiedName~EchoAgent_TemplateDeployActivateMessageDeactivateAndRemove"
dotnet test --filter "FullyQualifiedName~KnowledgeAgent_SystemUpload_TenantAndActivationOverridesIsolate"
dotnet test --filter "FullyQualifiedName~KnowledgeListAgent_ListAsync_AgentScoped"
dotnet test --filter "FullyQualifiedName~SecretVaultAgent_CreateFetch_StrictScopeIsolationAndRotation"
dotnet test --filter "FullyQualifiedName~SecretVaultSdkAgent_CollectionOps"
dotnet test --filter "FullyQualifiedName~DocumentDbAgent_SavePush_AdminReadModifyAdd_Isolates"
dotnet test --filter "FullyQualifiedName~DocumentDbSdkAgent_QueryGetUpdateExistsDelete"
dotnet test --filter "FullyQualifiedName~WebhookAgent_InboundBuiltin_AdminCrudIsolatesAndRevokes"
dotnet test --filter "FullyQualifiedName~WebhookSdkAgent_DeleteAsync_AndNon200Response"
dotnet test --filter "FullyQualifiedName~FileMessagingAgent_UserUploadAndAgentSend_RoundTripAndIsolate"
dotnet test --filter "FullyQualifiedName~CustomWorkflowAgent_DefineCustom_StartExecuteSignalAndAdminOps"
dotnet test --filter "FullyQualifiedName~WorkflowHandleAgent_SignalWithStart_QueryAndComplete"
dotnet test --filter "FullyQualifiedName~SchedulerAgent_ActivableSetup_CreatesScheduleAndAdminOps"
dotnet test --filter "FullyQualifiedName~ScheduleSdkAgent_CollectionOps"
dotnet test --filter "FullyQualifiedName~ScheduleCreateAgent_StrictCreate_DescribeFromActivity"
dotnet test --filter "FullyQualifiedName~HitlTaskAgent_StartTaskWait_AdminProgressAndTimeout"
dotnet test --filter "FullyQualifiedName~HitlTaskSdkAgent_ProgressOps"
dotnet test --filter "FullyQualifiedName~HitlTaskConversationAgent_CreateAndWait_HitlTaskApprove_AndFireAndForget"
dotnet test --filter "FullyQualifiedName~HitlLastTaskIdAgent_StampAndGetLastTaskId_IsolatesParticipant"
dotnet test --filter "FullyQualifiedName~CrossAgentWorkflow_InvoiceCallsFraud_ActivationTargetAndValidation"
dotnet test --filter "FullyQualifiedName~ActivationSdkAgent_ManagerProvisionsTarget"
dotnet test --filter "FullyQualifiedName~MetricsAgent_HandlerAndWorkflowReport_AdminReadsAndIsolates"
dotnet test --filter "FullyQualifiedName~LoggingAgent_WorkflowAndActivityLogs_AdminReadsAndIsolates"
dotnet test --filter "FullyQualifiedName~MessagingSdkAgent_DataProgressProactiveAndHistoryIsolate"
dotnet test --filter "FullyQualifiedName~WorkflowFileMessagingAgent_SendFileAsSupervisor_RoundTrip"
dotnet test --filter "FullyQualifiedName~TenantScopedAgent_RegisterActivateMessageAndRemove"
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
- Knowledge `ListAsync` as the running agent lists tenant-scoped items (agent-scoped, no system fallback)
- Secret Vault strict scope as the running agent writes and reads it (`TenantScope()` / `FetchByKeyAsync` inside the supervisor)
- Secret Vault `GetByIdAsync` from an activity, and `ListAsync` / `DeleteAsync` from workflow code (Create/Fetch/GetById/Update refuse a workflow so the value never enters Temporal history)
- Document DB Type+Key as the running agent writes and reads it (`SaveAsync` / `GetByKeyAsync` inside the supervisor)
- Document DB `QueryAsync` / `GetAsync(id)` / `UpdateAsync` / `ExistsAsync` / `DeleteAsync` as the running agent calls them
- Builtin inbound webhooks as the running Integrator handles them (`OnWebhook` / `context.Respond`, SDK `Webhooks.CreateAsync`)
- Webhook SDK `DeleteAsync` and Integrator `WebhookResponse.NotFound` (HTTP 404)
- File messages both ways: user `POST .../send/file` into `OnFileUpload`, agent `ReplyWithFileAsync` / `SendFileAsync` back through GridFS
- Custom Temporal classes registered with `DefineCustom` and driven through `XiansContext.Workflows`
- `SignalWithStartAsync` (client-only) and typed `GetWorkflowHandleAsync` / `QueryAsync`
- Schedules created from an activable workflow (`CreateIfNotExistsAsync`) and managed through Admin HTTP
- `ScheduleCollection` CreateIfNotExists/Exists/List/Get/Pause/Unpause/Trigger/Delete from a Temporal activity and from workflow code (system `ScheduleActivities` stub)
- Strict schedule `CreateAsync` (throws if it exists) and activity-only `DescribeAsync`
- HITL tasks created from a workflow (`StartTaskAsync` / `GetResultAsync`) and progressed through Admin HTTP, including timeout
- HITL `TaskCollection` UpdateDraft/UpdateMetadata/PerformAction from an activity and from workflow code (system `TaskActivities` stub)
- HITL `CreateAndWaitAsync`, fire-and-forget `CreateAsync` (`SurviveParentClose`), and `HitlTask.FromWorkflowIdAsync` / `ApproveAsync`
- HITL `GetLastTaskIdAsync` after a chat message stamped with the task workflow id
- Cross-agent `XiansContext.Workflows` calls (`ExecuteAsync` / `StartAsync` / `SignalAsync` by `"OtherAgent:WorkflowName"`, including `activationName` targeting)
- Activation SDK `agent.Tenant.Agent(...)` Exists/Create/Activate/status/list/Deactivate from a Temporal activity and from workflow code (HTTP stubbed to `ActivationActivities`)
- Metrics `context.Metrics` / `XiansContext.Metrics` `ReportAsync` from a running agent, then Admin stats/categories
- Logging `XiansLogger.GetLogger` from a chat activity and `Workflow.Logger` from a workflow, then Admin streams/logs
- Data / progress / proactive messaging (`OnUserDataMessage`, `SendReasoningAsync` / `SendToolExecAsync`, `SendChatAsSupervisorAsync`, `GetChatHistoryAsync` topic isolation)
- File send from workflow code (`XiansContext.Messaging.SendFileAsSupervisorAsync`)
- Tenant-scoped agents that are not system templates (`IsTemplate = false`, `{tenantId}:{workflowType}` queue)

Echo is the chat/fan-out contract. Knowledge is the scoped-knowledge contract (fallback). Knowledge list is tenant `ListAsync` scoped to the agent. Secret Vault is the scoped-secret contract (strict match; List/Delete also from a workflow). Document DB is the agent's persistent JSON store (Type+Key, auto-scoped queries). Document DB SDK is Query/Get/Update/Exists/Delete on that store. Webhooks is the inbound Integrator contract (`POST /api/user/webhooks/builtin`). Webhook SDK is `DeleteAsync` and non-200 `WebhookResponse`. Files is the first-class `File` message contract from **handlers** (bytes in GridFS, `fileId` on the wire). Workflow files is the same File contract from **workflow** code (`SendFileAsSupervisorAsync`). Custom workflows is `DefineCustom` + Start / Execute / Signal plus Admin list/get/types/cancel. Workflow handle is client-only `SignalWithStartAsync` plus typed Query. Schedules is the [self-scheduling](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/scheduling/) contract (activable Setup creates a Tick interval; ScheduleCollection also runs from an activity and from a workflow; strict Create / Describe is a third cycle). HITL is the [human-in-the-loop](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/hitl-tasks/) contract (Review waits on a task; Admin draft/action/timeout; TaskCollection also progresses from an activity and from a workflow; CreateAndWait / fire-and-forget Create / HitlTask approve are a third cycle; GetLastTaskId is a fourth). Cross-agent is the [cross-agent workflows](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/cross-agent-workflows/) contract (Invoice starts Fraud Scan/Review; activations do not cross agent boundaries). Activations SDK is the [agents and activations](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/activations/) contract (Manager provisions Target from an activity and from a workflow; Heartbeat starts on activate). Metrics is the [usage tracking](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/metrics/) contract (`context.Metrics` from a handler, `XiansContext.Metrics` from a workflow; Admin reads the flattened `usage_metrics` collection). Logging is the [agent logging](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/logging/) contract (`XiansLogger` from an activity, `Workflow.Logger` from a workflow; Admin reads the `logs` collection after batched upload). Messaging SDK is the remaining [messaging](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-replying/) contract (Data, progress, proactive Supervisor chat, scoped history). Tenant-scoped is the [multitenancy](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/multitenancy/) non-template queue. None of these is a catalogue of every Lib sample.

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
| Task (HITL) | `{agentName}:Task Workflow` | n/a | `hitl_task:…` prefix | Echo: no (`EnableTasks = false`). HITL cycle: yes |

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

## Agent under test: Knowledge list

Same host. Admin POSTs tenant knowledge (`alpha`/`beta` on the owner, `gamma` on a second agent). Chat `"list"` replies with sorted names from `ListAsync`. List is tenant-scoped (no system fallback) and agent-scoped.

```text
1. Admin POST tenant knowledge alpha, beta on owner; gamma on other agent
2. Owner chat "list" → list:alpha,beta
3. Other agent chat "list" → list:gamma
```

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

## Agent under test: Secret Vault SDK

Same host as the other Lib cycles. Chat `ExecuteAsync` a Manage workflow that either runs TenantScope CRUD in an activity or List/Delete in workflow code (`fromWorkflow`). Create/Fetch/GetById/Update throw from a workflow because those payloads would be recorded in Temporal history. The activity path is the one that includes `GetByIdAsync` (the isolation cycle looks up ids from `ListAsync` only). Replies never include secret values.

```text
1. Chat "run" from activity → Create, Fetch, List, GetById, Update, GetById, Delete, Fetch/GetById missing
2. Chat "run" from workflow → activity seed Create; List finds it; Create/Fetch/GetById/Update throw; Delete; List no longer has it
```

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

## Agent under test: Document DB SDK

Same host. Chat `"run"` saves two Type+Key documents, then `QueryAsync` / `GetAsync(id)` / `ExistsAsync` / `UpdateAsync` / `DeleteAsync` / `DeleteManyAsync`. Chat `"save leftover gold"` plus another participant `"query leftover"` proves Query auto-scope.

```text
1. Chat "run" → Query 2, Get gold, Exists, Update platinum, Delete, Exists false, DeleteMany, Query 0 → run:ok
2. Chat "save leftover gold" as owner
3. Other participant "query leftover" → query:0
```

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

## Agent under test: Webhook SDK

Same host. Integrator always `Respond(WebhookResponse.NotFound("denied"))`. Chat `"create"` uses SDK Create; inbound POST is HTTP 404. Chat `"delete"` uses SDK `DeleteAsync`; the same URL is then 401.

```text
1. Chat "create Deny-…" → Admin list has the URL
2. POST that URL → 404 {"error":"denied"}
3. Chat "delete" → deleted:1; Admin list empty
4. POST the same URL → 401
```

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

## Agent under test: Workflow handle

Same host. Chat `"run"` is a Temporal activity, so `SignalWithStartAsync` uses the Temporal **client** path (it throws inside a workflow). First Ping starts Hold; typed `GetWorkflowHandleAsync(SafeIdPostfix)` queries status; second Ping hits the same running id; `CompleteAsync` finishes it.

```text
1. Chat "run" → SignalWithStart Ping "first" → Query first → SignalWithStart Ping "second" → Query second → Complete → run:ok:first:second
2. Admin list {tenant}:{agent}:Hold:{activation} Completed
```

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

## Agent under test: Schedule SDK

Same host as the other Lib cycles. Chat `ExecuteAsync` a Manage workflow that either runs `ScheduleCollection` in an activity or in workflow code (`fromWorkflow`), matching the activations SDK dual-context pattern. Create uses a 60s interval so the only expected fire during the test is `TriggerAsync`.

```text
1. Chat "run" from activity (and a second test from workflow)
2. Exists("ghost") is false; CreateIfNotExistsAsync("managed") twice (idempotent)
3. Exists/List/Get the created id; Pause → GetSnapshotAsync.Paused; Unpause → not paused
4. Pause again; TriggerAsync
5. Admin list has {tenant}:{agent}:front-desk:managed; history has at least one Tick
6. Chat "cleanup" → DeleteAsync; Exists false; GetAsync throws not-found
7. Admin GET by-id 404
```

Not covered here: `idPostfix` overloads, `XiansSchedule.UpdateAsync` / `BackfillAsync` / `GetHandle`. Strict `CreateAsync` and `DescribeAsync` are the Schedule create cycle.

## Agent under test: Schedule create

Same host. Chat `ExecuteAsync` a Manage workflow that always runs in an **activity** (`DescribeAsync` throws inside a workflow). Strict `CreateAsync` starts paused; a second Create throws `ScheduleAlreadyExistsException`; `DescribeAsync` confirms paused and the id.

```text
1. Chat "run" → CreateAsync paused, second Create throws, DescribeAsync paused → run:ok:
2. Admin list {tenant}:{agent}:front-desk:strict
3. Chat "cleanup" → DeleteAsync; Admin GET by-id 404
```

## Agent under test: HITL tasks

Same host as the other Lib cycles. `RegisterTemplate(..., enableTasks: true)` makes `RunAllAsync` call `WithTasks`, which registers `{agent}:Task Workflow` on `hitl_task:{agent}:Task Workflow`. Review is not activable — chat `StartAsync` starts it, and Review creates the task **inside** the workflow (`StartTaskAsync` is workflow-only) then `GetResultAsync` waits. Admin HTTP is the human side of [HITL tasks](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/hitl-tasks/).

```text
1. Chat "review {name}" → StartAsync Review (no timeout); reply waiting:{name}
2. Review StartTaskAsync; ID is {tenant}:{agent}:Task Workflow:{activation}--{name}
3. Admin list/get title, draft, availableActions; parent Review stays Running
4. PUT draft → GET finalWork updated, initialWork unchanged
5. PUT metadata → GET includes the merged key
6. Other tenant GET by-id 404; other tenant list does not include the id
7. POST action approve → GET completed + performedAction; parent Review Completed
8. Chat "expire {name}" → Review with Timeout = 2s
9. GET TimedOut=true, isCompleted=false, no performedAction, status Completed
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/tasks?agentName=…`
- `GET .../tasks/by-id?taskId=…`
- `PUT .../tasks/draft?taskId=…`
- `PUT .../tasks/metadata?taskId=…`
- `POST .../tasks/actions?taskId=…`

Stub HITL get/draft/metadata/action (no Lib agent) remains [`AdminApiTemporalScheduleAndTaskTests`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalScheduleAndTaskTests.cs).

## Agent under test: HITL SDK

Same host as the other Lib cycles, with `enableTasks: true`. Chat `ExecuteAsync` a Review workflow that always `StartTaskAsync` / `GetResultAsync` in workflow code (those APIs throw outside a workflow). Draft, metadata, and action go through `TaskCollection` either in an activity or in workflow code (`fromWorkflow`), which stubs to `TaskActivities`.

```text
1. Chat "run" from activity (and a second test from workflow)
2. Review StartTaskAsync; collection task id is the suffix after the last colon
3. UpdateDraftAsync / UpdateMetadataAsync / PerformActionAsync
4. GetResultAsync: FinalWork is the revised draft, action approve, metadata source present
```

Not covered here: `CreateAndWaitAsync`, fire-and-forget `CreateAsync`, or `HitlTask` approve. Isolation and timeout stay on the Admin HTTP cycle.

## Agent under test: HITL conversation

Same host, with `enableTasks: true`. Chat `StartAsync` a Wait workflow that `CreateAndWaitAsync`; chat `HitlTask.FromWorkflowIdAsync` + `ApproveAsync` completes it (title is the task name so the reply is `approved:{name}`). Chat `ExecuteAsync` a Forget workflow that fire-and-forget `CreateAsync` with `SurviveParentClose = true`; the parent completes while the task stays pending until the same approve path. Task IDs are `{tenant}:{agent}:Task Workflow:{activation}--{name}`. Agent names must not contain `:` (`FromWorkflowIdAsync` splits on colon).

```text
1. Chat "wait {name}" → Start Wait; Admin list has the parent and the task
2. Chat "approve {taskId}" → HitlTask ApproveAsync → approved:{name}; Wait Completed
3. Chat "forget {name}" → Execute Forget CreateAsync SurviveParentClose; parent Completed; task still pending
4. Chat "approve {taskId}" → task completed
```

## Agent under test: HITL last task

Same host, with `enableTasks: true`. Chat `"ask {name}"` `ExecuteAsync`es Ask (`CreateAsync` SurviveParentClose) then `SendChatAsSupervisorAsync` with `taskId` equal to `{tenant}:{agent}:Task Workflow:{activation}--{name}`. Chat `"last"` is `GetLastTaskIdAsync`. Another participant does not see that id.

```text
1. Owner chat "ask {name}" → stamped:{name}
2. Owner chat "last" → last:{tenant}:{agent}:Task Workflow:front-desk--{name}
3. Other participant "last" → last:none
```

## Agent under test: Cross-agent workflows

Same host as the other Lib cycles, with **two** templates (`StartWorkersAsync(invoice, fraud)`). Invoice has only a supervisor. Fraud registers `DefineCustom` Scan and Review (`Activable = false`). Invoice chat calls Fraud by type string (`"{fraud}:Scan"` / `"{fraud}:Review"`), which is the [cross-agent](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/cross-agent-workflows/) contract: the target worker is derived from the type, but Invoice's `front-desk` activation is not inherited. Pass `activationName` to target `fraud-eu`. `uniqueKey` is required when there is no activation postfix.

```text
1. Chat "scan {id}" → ExecuteAsync Fraud Scan, uniqueKey only
2. Admin list on Fraud: {tenant}:{fraud}:Scan:{id} Completed
3. Inherited ID {tenant}:{fraud}:Scan:front-desk:{id} does not exist; Invoice list does not include the Scan
4. Chat "scan-eu {id}" → ExecuteAsync with activationName=fraud-eu → {tenant}:{fraud}:Scan:fraud-eu:{id}
5. Chat "hold" → StartAsync Review under fraud-eu; SignalAsync ApproveAsync with activationName
6. Chat "missing" → activationName that was never created → not-found
7. Chat "dead" → fraud-us deactivated → deactivated
8. Other tenant GET of the owner's Scan id 404
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/workflows?workflowId=…`
- `GET .../workflows/list?agent=…&status=…`
- `POST .../agentActivations/{id}/deactivate`

## Agent under test: Activations SDK

Same host, two templates (`StartWorkersAsync(manager, target)`). Manager chat `ExecuteAsync`es Lifecycle, which calls `manager.Tenant.Agent(target)` — the [agents and activations](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/activations/) APIs. Two Facts share that cycle: one runs the SDK from a Temporal **activity**, the other from **workflow** code (Lib stubs HTTP to the registered `ActivationActivities`). Target Heartbeat is `Activable = true`, so SDK `ActivateAsync` starts `{tenant}:{target}:Heartbeat:{sdk-demo}`. Agent API has no delete; `DeactivateAsync` is the remove step. Permanent delete is still Admin.

```text
1. Chat "exists" → ExistsAsync(target) true (deployed, not Admin-activated)
2. Chat "missing" → ExistsAsync(ghost agent) false
3. Chat "self" → manager.ActivationExistsAsync() true (front-desk from context)
4. Chat "status" → GetActivationStatusAsync(sdk-demo) NotFound
5. Chat "provision" → CreateActivationAsync (inactive, chat participantId) then ActivateAsync
6. Admin list sdk-demo active; Heartbeat Running
7. Other tenant exists true, status NotFound; Admin GET owner's activation id 404
8. Chat "deactivate" → DeactivateAsync; status Deactivated; Heartbeat Canceled
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/agentActivations?agentName=…`
- `GET .../agentActivations/{id}`
- `GET /api/v1/admin/tenants/{tenant}/workflows?workflowId=…`

## Agent under test: Metrics

Same host, two templates: the Metrics agent (`DefineCustom<MetricsReportWorkflow>`) and a supervisor-only isolation agent (same C# workflow class is not registered twice — `GetWorkflowTypeFor(typeof(T))` returns the first match). Chat `"handler"` reports the [metrics](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/metrics/) token example through `context.Metrics` (`ForModel("gpt-4")`). Chat `"workflow"` `ExecuteAsync`es `{agent}:MetricsReport` by type string, which reports approvals/documents through `XiansContext.Metrics` (system `UsageActivities` stub). Admin stats/categories read the flattened `usage_metrics` rows. Another tenant and another agent see zero records. DELETE by activation name clears the owner's rows. Mongo-only Admin seeding remains `AdminMetricsEndpointsTests`. Time-series is not asserted here: Mongo2Go is 4.4 and the Mongo path uses `$dateTrunc` (see `UsageEventRepositoryTimeSeriesTests`).

```text
1. Chat "handler" → context.Metrics tokens prompt/completion/total (gpt-4)
2. Chat "workflow" → XiansContext.Metrics approvals/submitted + documents/generated
3. Admin stats: tokens.total=150, three token types + two business types
4. Admin categories include tokens and approvals
5. Admin stats?model=gpt-4 is the three token rows only
6. Other tenant / other agent stats 0
7. DELETE .../metrics/agents/{agent}/activation/front-desk → stats 0
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/metrics/stats?agentName=…&startDate=…&endDate=…`
- `GET .../metrics/categories?agentName=…`
- `DELETE .../metrics/agents/{agent}/activation/{activationName}`

## Agent under test: Logging

Same host, two templates: the Logging agent (`DefineCustom<LoggingProbeWorkflow>`) and a supervisor-only isolation agent. The host opts into server upload (`ServerLogLevel = Information`) and a 250 ms batch interval; other Lib cycles keep `ServerLogLevel = None`. Chat `"activity {token}"` logs from the supervisor handler (a Temporal activity) with `XiansLogger.GetLogger`. Chat `"workflow {token}"` `ExecuteAsync`es `{agent}:LogProbe`, which logs with `Workflow.Logger`. Admin streams/logs read the uploaded `logs` rows. `logLevel=Warning` returns the activity warning only. Another tenant and another agent do not see the token. DELETE by activation name clears the owner's rows. Mongo-only Admin seeding remains `AdminLogsEndpointsTests`.

```text
1. Chat "activity {token}" → XiansLogger Information + Warning
2. Chat "workflow {token}" → Workflow.Logger Information
3. Admin logs by supervisor workflow id contain activity-log and activity-warn
4. Admin logs by LogProbe workflow id contain workflow-log
5. Admin streams list both workflow ids
6. Admin logs?logLevel=Warning is the warning only
7. Other tenant / other agent do not contain the token
8. DELETE .../logs/agents/{agent}/activation/front-desk → token gone
```

Admin HTTP used beyond the shared deploy/activate helpers:

- `GET /api/v1/admin/tenants/{tenant}/logs?agentName=…&workflowId=…`
- `GET .../logs/streams?agentName=…`
- `GET .../logs?agentName=…&logLevel=Warning`
- `DELETE .../logs/agents/{agent}/activation/{activationName}`

## Agent under test: Messaging SDK

Same host as the other Lib cycles. Supervisor handles chat **and** data. Chat `"progress"` streams reasoning then tool then a final chat reply. Chat `"notify"` `ExecuteAsync`es `{agent}:Notify`, which calls `XiansContext.Messaging.SendChatAsSupervisorAsync`. Chat `"history {needle}"` replies with whether `GetChatHistoryAsync` saw that needle in the current message scope. Product behaviour: [Reply](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-replying/), [Proactive](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-proactive/), [Progress](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-progress/).

```text
1. Admin POST send type=Data { sku } → OnUserDataMessage SendDataAsync data-ok:{sku}; history outgoing messageType=Data
2. Chat "progress {marker}" → SendReasoningAsync thinking: / SendToolExecAsync tool: / ReplyAsync done:
   History outgoing Reasoning + Tool + Chat; Admin SSE contains thinking:
3. Chat "notify {marker}" → ExecuteAsync Notify → SendChatAsSupervisorAsync proactive:{marker}; reply notified:
4. Chat "seed {default}" (no topic) then "seed {alerts}" topic=alerts
   topic=alerts "history {default}" → history-miss; "history {alerts}" → history-hit
```

## Agent under test: Workflow files

Same host. Chat `"report {marker}"` `ExecuteAsync`es `{agent}:File Report`, which calls `SendFileAsSupervisorAsync` so the file lands in Supervisor history. Handler send stays on the Files cycle. Product behaviour: [File upload](https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-fileupload/).

```text
1. Chat "report {marker}" → ExecuteAsync File Report
2. History caption "generated {marker}"; outgoing File has workflow-report.txt fileId and no content
3. Admin GET .../messaging/files/{id} equals the marker bytes
```

## Agent under test: Tenant-scoped

`host.RegisterTenant` sets `IsTemplate = false`. Lib uploads a tenant agent (no `agentTemplates` record). There is no deploy step. The worker queue is `{tenantId}:{agent}:Supervisor Workflow`. Admin send uses that queue because `AgentRepository.IsSystemAgent` is false.

```text
1. RegisterTenant + RunAllAsync; WaitForTenantAgentAsync (GET agentDeployments/{name} + tenant flow defs)
2. Activate (no template deploy)
3. Chat round-trip TenantEcho: {text}
4. GET agentTemplates/by-name → 404
5. Deactivate, DELETE agentDeployments/{name}?forceDelete=true
```

## Test harness around Lib

Lib's HTTP client uses `SocketsHttpHandler`. It cannot be given `TestServer.CreateHandler()`. [`TestServerLoopback`](../../../XiansAi.Server.Tests/TestUtils/TestServerLoopback.cs) binds `HttpListener` on `127.0.0.1:{ephemeral}` and forwards to the in-process TestServer. [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) owns that loopback.

Lib API keys are a base64 PFX whose subject is `CN={user}, OU={user}, O={tenant}`. [`XiansLibTestCertificate`](../../../XiansAi.Server.Tests/TestUtils/XiansLibTestCertificate.cs) builds that key. Certificate policies on the host are remapped to `TestAuthHandler` (see [Host and fixtures](./host.md)).

`ITenantContext` is a Moq singleton. `BindTenantContext` assigns `TenantId`, `LoggedInUser`, `ParticipantId`, and roles so Lib uploads and Admin HTTP agree. Agent API requests that send `X-Tenant-Id` also copy that tenant onto the mock (`TestAuthHandler`), because certificate auth is remapped in tests. SDK `CreateActivationAsync` must pass `participantId` so activate can start workflows without relying on that racy `LoggedInUser`.

Lib keeps process-wide statics (handlers, definition-upload cache). The host calls `TestCleanup.ResetAllStaticState()` and `WorkflowDefinitionUploader.ResetCache()` on start and dispose, and cancels `RunAllAsync`.

`XiansPlatform.InitializeAsync` is given the loopback URL, the PFX key, `EnableTasks = false` (the HITL cycle opts in per agent with `RegisterTemplate(..., enableTasks: true)`), `Cache.Enabled = false`, `ServerLogLevel = None` unless a cycle opts in (Logging uses `Information` plus a 250 ms upload interval), and `TemporalConfiguration` pointing at `TemporalFixture` (same local CLI as the rest of the collection).

## What these cycles do not cover

Documented Agent SDK methods that are still **not** worth a Temporal Lib cycle:

Skip for Lib server tests:

- Unit Testing page (`InitializeForTestsAsync`) — local mode, belongs in Xians.Lib tests
- Operating Context registry helpers
- Convenience twins of covered APIs (`WithMetric`, `ReplyWithFilesAsync`, `UploadEmbeddedResourceAsync`)
- Secret `ScopeUnbound()`, Schedule `UpdateAsync` / `BackfillAsync` / `GetHandle`
- A2A (not on the public concepts overview)
- Legacy Temporal Update webhooks (`POST /api/user/webhooks/{workflow}/{methodName}`)

Keep new coverage as separate tests on `LibAgentWorkflowHost`. Do not grow Echo, Knowledge, Knowledge list, Secret Vault, Secret Vault SDK, Document DB, Document DB SDK, Webhooks, Webhook SDK, Files, Workflow files, Custom workflows, Workflow handle, Schedules, Schedule SDK, Schedule create, HITL, HITL SDK, HITL conversation, HITL last task, Cross-agent, Activations SDK, Metrics, Logging, Messaging SDK, or Tenant-scoped into a second sample.

## Adding another Lib agent workflow

Reuse [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) and the helpers on `AdminApiTemporalIntegrationTestBase`:

1. Stay in the `AdminApiTemporal` collection and `AdminApiTemporalIntegrationTestBase`.
2. `await using var host = await LibAgentWorkflowHost.StartAsync(...)`; `BindTenantContext`.
3. `host.RegisterTemplate` with a unique name. Use `IsTemplate = true` if Admin send should hit the system queue. Use `host.RegisterTenant` + `WaitForTenantAgentAsync` (no deploy) for a tenant-only worker.
4. Define only the workflows (and knowledge / secrets / documents / webhooks / files / custom types / schedules / tasks / extra agents / metrics / logs) the assertion needs.
5. `StartWorkersAsync` then `WaitForTemplateAsync` before deploy. The wait is for the template **and** a stable set of system-scoped flow definitions — the agent record alone is not enough to activate.
6. Drive the public Admin API; poll history or list endpoints instead of a single Temporal visibility read.
7. Dispose of the host (cancels workers and resets Lib statics).

If the new agent is tenant-scoped only (`IsTemplate = false`) and no system template of that name exists, the worker queue is `{tenantId}:{workflowType}` and SignalWithStart must use the same. Do not mix a system template of the same name with a tenant worker.
