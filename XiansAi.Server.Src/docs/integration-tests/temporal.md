# Temporal tests

Mongo-only Admin tests mock `ITemporalGatewayService`. Paths that must talk to Temporal opt in through the `AdminApiTemporal` collection, which starts **one** local Temporal CLI/dev server for the whole collection.

xUnit does not run classes in the same collection in parallel, so workflow ids stay unique without extra locking. Mongo-only tests are **not** in this collection and do not pay the CLI startup cost.

## How to run

```bash
dotnet test --filter "FullyQualifiedName~AdminApiTemporal"
```

The Echo / Knowledge / Secret Vault / Document DB / Webhooks / Files / Custom workflow Xians.Lib cycles are documented separately: [Lib agent workflows](./lib-agent-workflows.md).

### Temporal CLI

[`TemporalFixture`](../../../XiansAi.Server.Tests/TestUtils/TemporalFixture.cs) calls `WorkflowEnvironment.StartLocalAsync` (`Temporalio.Testing`) with namespace `default` and the platform search attributes (`TenantId`, `Agent`, `UserId`, `IdPostfix`).

The first run may download the Temporal CLI into the user cache. Later runs reuse it. In CI or air-gapped environments, point at an existing binary:

```bash
export XIANS_TEMPORAL_CLI_PATH=/usr/local/bin/temporal
# or
export TEMPORAL_CLI_PATH=/usr/local/bin/temporal
```

## Collection and host wiring

[`AdminApiTemporalCollection`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalCollection.cs) is an `ICollectionFixture<TemporalFixture>`. Test classes in the collection take `(MongoDbFixture, TemporalFixture)` and pass both to [`AdminApiTemporalIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalIntegrationTestBase.cs).

When the factory receives a `TemporalFixture` it:

- Sets `Temporal:FlowServerUrl` / `Temporal:FlowServerNamespace` after environment variables
- Leaves `ITemporalGatewayService` and `IActivationCleanupService` as the real implementations
- Makes `ITenantContext.GetTemporalConfigAsync()` return the fixture host and namespace

That last mock matters: tenant Temporal config in Mongo is not used for these tests; traffic always hits the local CLI.

## Stub worker vs Xians.Lib

Two ways a workflow actually runs:

| Mechanism | When to use | Types |
| --- | --- | --- |
| In-process stub | Admin HTTP against a known workflow type without the SDK | [`StubAgentWorkflow`](../../../XiansAi.Server.Tests/TestUtils/StubAgentWorkflow.cs), [`StubReplyActivities`](../../../XiansAi.Server.Tests/TestUtils/StubReplyActivities.cs), [`TemporalTestWorker`](../../../XiansAi.Server.Tests/TestUtils/TemporalTestWorker.cs) |
| Xians.Lib (Echo, Knowledge, Secret Vault, Document DB, Webhooks, Files, Custom workflows) | Full system-template lifecycle the way production agents are authored | [Lib agent workflows](./lib-agent-workflows.md) |

The stub worker listens on a tenant queue `{tenantId}:{workflowType}`. Start it with `StartWorkerAsync(ChatTaskQueue(tenantId, flow.WorkflowType))` from the Temporal base class.

## Test classes

### `AdminApiTemporalEndpointsTests`

No worker. Covers Temporal HTTP that is valid against an empty or started-then-cancelled cluster:

- `POST .../temporal-config/test-connection`
- List workflows / types (empty page)
- Get missing workflow (400)
- List/delete schedules when none exist
- List HITL tasks (empty)
- Stats with a date range (zero counts)
- List/describe worker deployments
- Activate → get → events → cancel without a worker

### `AdminApiTemporalLifecycleTests`

Starts workflows through Admin HTTP (and a stub worker where a query/signal must complete):

- Activate / deactivate an activation (pass a real `participantId`; an empty string is sanitized and then rejected as a Temporal user id)
- Send a message and read it back from messaging history
- Heartbeat when the stub worker replies
- Workflow types after a start

### `AdminApiTemporalScheduleAndTaskTests`

- Schedule create → list → pause → resume → delete (and delete-by-activation)
- HITL task get / draft / metadata / action / stats
- Worker deployment list / describe / promote (versioned stub worker)

### `AdminApiTemporalEchoAgentLifecycleTests`

System Echo agent authored with Xians.Lib (supervisor + `ReplyAsync($"Echo: …")`), then Admin deploy / activate / chat / teardown. Details, queues, and the workflow type Lib starts: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalKnowledgeAgentLifecycleTests`

System Knowledge agent authored with Xians.Lib (supervisor replies with `GetAsync("playbook")`). Admin tenant override, second tenant still sees system original; activation override, second agent and second activation still see the less-specific copy. Same host as Echo: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalSecretVaultAgentLifecycleTests`

System Secret Vault agent authored with Xians.Lib. Chat commands create / fetch / update / delete via `XiansContext.CurrentAgent.Secrets` with no-arg scopes from live context. Fetch is a **strict** match (tenant / agent / participant / activation) — unlike Knowledge, there is no fallback. Admin list/fetch return metadata only. Same host: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalDocumentDbAgentLifecycleTests`

System Document DB agent authored with Xians.Lib. Chat saves Type+Key JSON (`SaveAsync` / `GetByKeyAsync`); Admin `/tenants/{tenant}/data` lists, gets, updates, and creates records the agent can then read. Queries from chat are auto-scoped to agent, activation, and participant. Same host: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalWebhookAgentLifecycleTests`

System webhook agent authored with Xians.Lib (`DefineIntegrator` + `OnWebhook`, supervisor `agent.Webhooks.CreateAsync`). Admin `/tenants/{tenant}/webhooks` lists, creates, and deletes. Inbound `POST /api/user/webhooks/builtin` authenticates with `apikeyId` (UserApi policy is not stubbed) and waits for `context.Respond`. Other tenants and agents do not share the owner's URL; delete revokes the key (401); deactivate returns 409. Same host: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalFileMessagingAgentLifecycleTests`

System file-messaging agent authored with Xians.Lib (`OnFileUpload` / `ReplyWithFileAsync` / `SendFileAsync`). Admin `POST .../messaging/send/file` stores bytes in GridFS and signals `fileId` references; the handler hydrates bytes and sends a file back. Chat `SendFileAsync` is the agent-originated direction. History has refs only; Admin download is tenant-scoped. Same host: [Lib agent workflows](./lib-agent-workflows.md).

### `AdminApiTemporalCustomWorkflowAgentLifecycleTests`

System custom-workflow agent authored with Xians.Lib (`DefineCustom` + `XiansContext.Workflows`). Admin activate starts the `Activable` Onboarding workflow. Chat `ExecuteAsync` / `StartAsync` / `SignalAsync` start Inventory Check, Payment, and Approval. Admin list/get/types/cancel those runs; a second tenant cannot GET the owner's workflow id. Same host: [Lib agent workflows](./lib-agent-workflows.md).

## Seeding Temporal tests

Use the Temporal base helpers rather than starting workflows with the Temporal client unless the case is specifically about schedules or HITL (those helpers exist too):

- `SeedTenantAgentAndFlowAsync()` — tenant, agent, built-in flow definition
- `SeedActiveActivationAsync()` — plus an active activation
- `WaitForWorkflowInListAsync` — poll Admin list until visibility catches up
- `CreateAgentScheduleAsync` / `StartHitlTaskAsync` — schedule and HITL seed data

Always pass `participantId` when creating activations that will start workflows.

## Live streams

The Echo cycle asserts the change-stream fan-out on Admin SSE, UserApi SSE (`/api/user/sse/events`), tenant SignalR (`/ws/tenant/chat`), and ChatHub (`/ws/chat`). See [Lib agent workflows](./lib-agent-workflows.md).

## Adding a Temporal test

1. Put the class in `IntegrationTests/AdminApi/` and inherit `AdminApiTemporalIntegrationTestBase`.
2. Annotate with `[Collection(AdminApiTemporalCollection.Name)]` (the base already does; keep it on the concrete class if you copy an existing file).
3. Accept both fixtures in the constructor.
4. Prefer Admin HTTP as the public contract; use `Temporal.Environment.Client` only to seed cluster state the API cannot create.
5. Poll with a short delay (the existing 250 ms / 20-attempt helpers) instead of a single read of Temporal visibility.
6. Keep the test out of this collection if Temporal is not required for the assertion.
