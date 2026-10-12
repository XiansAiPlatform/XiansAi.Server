# Automated Agent Health Checks — Technical Design

Status: **Implemented** (branch `feature/agent-health-checks`; review decisions in §11)  ·  Scope: server only (AdminApi + background execution)  ·  UI: separate AgentStudio task (Q5)

## 1. Problem

The only production liveness signal today is the AdminApi heartbeat (`AdminHeartbeatEndpoints`), which tells you whether a
worker answers, not whether the agent behaves correctly. Tenant admins have no way to detect broken chat responses,
broken inbound webhooks or dead schedules without manual smoke-testing after each deploy.

This design adds **health checks**: test definitions attached to an `AgentActivation` that the server runs periodically,
recording a pass/fail history that can be queried through the AdminApi.

### In scope
- CRUD for health-check definitions (chat / webhook / schedule) attached to an `AgentActivation` (R1, Q4)
- Periodic execution driven by Temporal (R4)
- Persisted run results (status, timestamp, detail) plus a query API (R4, Q1)
- Gating by a single new capability (R6)

### Out of scope
- Alerting/notifications (Q1, follow-up issue). The design leaves a hook point for it (§9).
- AgentStudio UI (Q5)
- On-demand/synchronous triggering, regex or non-deterministic (LLM-judged) assertions

## 2. Decisions carried in

| # | Decision | Effect on design |
|---|---|---|
| Q1 | Health status and history only, no alerts | Results are stored and summarized; nothing is emitted |
| Q2 | Webhook test uses the inbound trigger path | Runs `IWebhookReceiverService.ProcessWebhook` (Temporal update-with-start), not `WebhookDispatcherService` |
| Q3 | Schedule test is a metadata check | Reads `IScheduleService`; never triggers a run |
| Q4 | Tests attach to an `AgentActivation` | Definitions are keyed by `activationId` and inherit tenant/agent/activation name from it |
| Q5 | Server only | AdminApi endpoints only; documented through the Swashbuckle-generated OpenAPI at `/api-docs` |
| R5 | `contains` / `notContains`, case-insensitive | Single assertion evaluator shared by chat and webhook tests |
| R6 | One capability | `tenant.healthChecks.manage` gates both configure and view |
| R7 | Per-test interval, minimum 5 min | Validated on create/update; Temporal schedule interval = test interval |
| R8 | Deactivate/delete disables tests | Pause on deactivate, resume on activate, delete on delete. A run-time guard covers races. |

## 3. Architecture

```
AdminApi (CRUD, history)                  Temporal (platform namespace, "Temporal" root config)
  │                                         ┌──────────────────────────────────────────┐
  │ create/update/delete                    │ Schedule  healthcheck:{tenantId}:{checkId}│
  ├──────────────► AgentHealthCheckService ─┤   every N min, overlap = Skip             │
  │                    │                    │   action: start AgentHealthCheckWorkflow  │
  │                    ▼                    └──────────────────┬───────────────────────┘
  │          Mongo: agent_health_checks                        │ task queue: xians-server-health-checks
  │          Mongo: agent_health_check_runs                    ▼
  │                    ▲                    ┌──────────────────────────────────────────┐
  │ query history      │ persist result     │ Server-hosted Temporal worker            │
  └────────────────────┴────────────────────┤  AgentHealthCheckWorkflow (thin)         │
                                            │   └─ activity RunHealthCheckAsync(checkId)│
                                            │        → IHealthCheckExecutor (by type)   │
                                            └──────────────────────────────────────────┘
```

### 3.1 New: the server hosts a Temporal worker

Today the server is a Temporal **client only**. "Temporal-driven execution" means it has to run a worker:

- A `HealthCheckWorkerService : BackgroundService` creates a `TemporalWorker` (included in the `Temporalio` package
  already referenced, so no new dependency) on task queue `xians-server-health-checks`. It registers one workflow and one
  activity class.
- It connects with the **platform-default** Temporal config (the root `Temporal` section), **not** tenant-override
  clusters. Health-check schedules for every tenant live in the platform namespace:
  - The server can't sensibly run a worker against every tenant's dedicated cluster.
  - Tenant namespaces get purged by `ActivationCleanupService` (search-attribute-based schedule deletion). Keeping our
    schedules out of them avoids accidental deletion and keeps them out of the tenant's schedule listings.
- The activity still reaches the agent through the tenant's own cluster via the existing `ITemporalGatewayFactory`, so
  tenants with dedicated clusters still work.
- Multiple server instances all poll the same queue. Temporal hands each run to exactly one instance, so we need no
  leader election or claim/lease logic (unlike the Mongo outbox in `WebhookDispatcherService`).
- Gated by config (`HealthChecks:Enabled`, default `true`). When it's disabled, the worker doesn't start and create/update
  calls return `503`.

**Alternative considered:** a Mongo-polled `BackgroundService` modeled on `WebhookDispatcherService`. It's simpler to
deploy, but it reimplements scheduling, overlap prevention and multi-instance claiming that Temporal Schedules provide,
and it contradicts the issue's stated direction. Rejected.

### 3.2 Workflow and activity

```csharp
[Workflow("Xians.AgentHealthCheck")]
public class AgentHealthCheckWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string tenantId, string checkId) =>
        Workflow.ExecuteActivityAsync(
            (HealthCheckActivities a) => a.RunHealthCheckAsync(tenantId, checkId),
            new() { StartToCloseTimeout = TimeSpan.FromMinutes(5), RetryPolicy = new() { MaximumAttempts = 1 } });
}
```

- **No activity retries.** A failing check is a result to record, not an error to retry. Retries would hide flakiness
  and multiply the cost of LLM calls.
- The activity is the only place with side effects. Its logic lives in `HealthCheckRunner`, which it calls after:
  1. Opening a DI scope and filling in the scoped `ITenantContext` with a **system principal**
     (`HealthCheckSystemPrincipal`): `TenantId` = check's tenant, `LoggedInUser` = `system:health-check`,
     `UserRoles` = `[TenantAdmin]`, `AuthorizedTenantIds` = `[tenant]`. The TenantAdmin role is scoped to that one tenant
     and mirrors the only role allowed to configure checks. Agent-permission checks (for example reading a schedule)
     need it. Existing services (`IMessageService`, `IWebhookReceiverService`, `IScheduleService`,
     `ITemporalGatewayFactory`) are tenant-context driven, so they work unchanged.

  The runner then:
  2. Loads the check and returns early (writes nothing) if the check is missing or disabled, **or the activation is
     missing or inactive**. This satisfies "runs are skipped rather than erroring" even when a pause raced with a
     schedule firing.
  3. Resolves the executor by type, runs it with the check's timeout, and maps exceptions to `Error` results. The
     activity itself never throws for check failures.
  4. Writes the run document, then updates the denormalized status on the check (§4).

### 3.3 Schedule lifecycle (`IHealthCheckScheduler`)

| Event | Temporal action |
|---|---|
| Check created | `CreateScheduleAsync("healthcheck:{tenantId}:{checkId}")`: `ScheduleIntervalSpec(every = interval)`, overlap `Skip`, catch-up window 1 min (no burst of missed runs after an outage), jitter `min(60s, interval/10)` to spread load, paused unless the check is enabled **and** its activation is active. Each run has a 10-minute execution timeout, so a run nobody picked up can't block later runs under `Skip` |
| Check updated | `UpdateAsync`: replace spec, action and paused state |
| Check deleted | `DeleteAsync` (a not-found error is ignored) |
| Activation **deactivated** | Pause every schedule of that activation's checks, with note `"activation deactivated"` |
| Activation **activated** | Unpause schedules of checks with `enabled = true` |
| Activation **deleted** | Delete checks, runs and schedules (added to `ActivationService.CleanupActivationDataAsync`) |

Temporal aligns interval schedules to the clock (multiples of the interval since the Unix epoch), not to when the
schedule was created. A 15-minute check created at 11:20 first runs in the 11:30 slot (plus jitter), then every
15 minutes, so the first run can come sooner than one full interval.

Mongo is the source of truth. A **reconciler** (`HealthCheckReconciler`, first pass 30s after the worker starts, then
every `ReconcileIntervalMinutes`) closes the gaps left by partial failures:
- A check whose schedule is `pending`, missing from Temporal, or in the wrong paused state is re-synced.
- A check whose activation no longer exists is deleted along with its runs (activation-delete cleanup failed).
- Schedules with the `healthcheck:` prefix that have no matching check are deleted.

Every step is idempotent, so several server instances reconciling at once is harmless.

That keeps the API simple. If the Temporal call fails after the Mongo write, the API still returns `201`, the check
gets `scheduleState = "pending"`, and the reconciler fixes it. We don't need a distributed transaction.

## 4. Data model

### 4.1 `agent_health_checks`

```jsonc
{
  "_id": ObjectId,
  "tenant_id": "acme",
  "activation_id": "66f…",          // AgentActivation.Id (Q4)
  "agent_name": "Support Agent",    // denormalized from activation at create
  "activation_name": "prod-eu",     // denormalized; needed to build the workflowId
  "name": "Greets user",            // 1–100 chars, unique per activation
  "type": "chat" | "webhook" | "schedule",
  "enabled": true,
  "interval_minutes": 15,           // >= 5 (R7), <= 1440
  "timeout_seconds": 60,            // 5–120 (chat/webhook only)

  "chat":     { "workflow_type": null,               // null = activation's conversational workflow
                "message": "Hello",                  // <= 4000 chars
                "assertions": [ { "op": "contains", "value": "hello" } ] },
  "webhook":  { "workflow_type": "Support Agent:Inbound",
                "method_name": "ticket-created",
                "query_params": { "source": "health-check" },
                "body": "{\"id\":\"test\"}",         // <= 32 KB
                "expected_status_code": 200,         // optional
                "assertions": [ { "op": "notContains", "value": "error" } ] },
  "schedule": { "schedule_id": "acme:Support Agent:…",
                "require_last_run_success": true },

  // denormalized health (written by the activity)
  "last_run_at": ISODate, "last_status": "pass" | "fail" | "error" | null,
  "consecutive_failures": 0, "last_success_at": ISODate,
  "schedule_state": "active" | "paused" | "pending",

  "created_by": "user@acme", "created_at": ISODate, "updated_by": "…", "updated_at": ISODate
}
```

Exactly one of `chat` / `webhook` / `schedule` is set, matching `type`. Request validation is in `HealthCheckValidator`
(a pure function: shape, limits, interval floor, assertion rules). Checks that need I/O run in the service: a chat
`workflowType` must be a conversational workflow of the agent, and a schedule check's `scheduleId` must exist and
belong to the activation's agent. Names use the same character rules as activation names.

In the API, enum values are camelCase strings (`chat`, `pass`, `notContains`, `active`). In Mongo they are stored as the
enum member name (`Chat`, `Pass`, ...).

Assertions (R5): 1–10 per check, `op` ∈ {`contains`, `notContains`}, `value` 1–500 chars, compared with
`StringComparison.OrdinalIgnoreCase`. A run passes only when **all** assertions pass.

### 4.2 `agent_health_check_runs`

```jsonc
{
  "_id": ObjectId,
  "tenant_id": "acme", "check_id": "…", "activation_id": "…", "type": "chat",
  "status": "pass" | "fail" | "error",
  "started_at": ISODate, "duration_ms": 1834,
  "detail": "Assertion failed: expected to contain 'hello'",   // <= 1000 chars
  "response_snippet": "Hi! How can I…",                         // first 500 chars of the response, sanitized
  "assertion_results": [ { "op": "contains", "value": "hello", "passed": false } ],
  "workflow_run_id": "…"                                        // Temporal run of the health-check workflow
}
```

- `fail` means the agent answered but the answer was wrong (an assertion failed, the status code didn't match, or the
  schedule was paused or its last run failed).
- `error` means the check couldn't get an answer: timeout, no worker, workflow not found, or a server-side exception.

  The UI will want to separate "agent is wrong" from "agent is down".

### 4.3 Indexes (`mongodb-indexes.yaml`)

| Collection | Index | Purpose |
|---|---|---|
| `agent_health_checks` | `{tenant_id:1, activation_id:1}` | list per activation |
| `agent_health_checks` | `{tenant_id:1, activation_id:1, name:1}` unique | name uniqueness |
| `agent_health_checks` | `{tenant_id:1, last_status:1}` | tenant-wide "failing" view |
| `agent_health_check_runs` | `{tenant_id:1, check_id:1, started_at:-1}` | history paging |
| `agent_health_check_runs` | `health_check_run_ttl`: `{started_at:1}` TTL **30 days** by default | bounded growth |

Retention is configurable per deployment through the existing TTL override (see `INDEXES_AND_TTL.md`):
`MongoIndexes__agent_health_check_runs__health_check_run_ttl__ExpireAfter=60d`.

At a 5-minute minimum interval, one check generates at most 8,640 runs over 30 days.

## 5. Executors (`IHealthCheckExecutor`, one per type)

### 5.1 Chat
Reuses the heartbeat's sync-message path (`AdminHeartbeatEndpoints` → `SyncMessageHandler`):
1. Resolve the workflow type: `chat.workflow_type`, or else `IActivationValidationService.ResolveConversationalWorkflowAsync`.
2. `ValidateActivationAsync`. If this fails, record `error` (or skip if the activation went inactive).
3. Build the workflow id with `WorkflowIdentifier.BuildWorkflowId(tenant, agent, workflowType, activationName)`. The
   participant is **`xians-health-check`**, so health-check conversations get their own thread and don't mix with real
   users' threads.
4. `ProcessSyncMessageAsync(request, MessageType.Chat, timeout)`. An `IResult` or a timeout becomes `error`. Otherwise
   evaluate the assertions against `response.Text`.

### 5.2 Webhook (Q2: inbound path)
1. Build the workflow identifier from `webhook.workflow_type` + `activation_name`, in the same format the inbound
   endpoint expects.
2. Call `IWebhookReceiverService.ProcessWebhook(tenantId, workflowId, methodName, queryParams, body)`. This is exactly
   the code behind `POST /api/user/webhooks/{workflow}/{methodName}` (update-with-start), minus HTTP and API-key auth.
3. `RequestTimeout` / `InternalServerError` results become `error`. On success, check `expected_status_code` (if set)
   against `WebhookResponse.StatusCode`, then the assertions against `WebhookResponse.Content`.
4. **Pass the check's timeout through.** `ProcessWebhook` currently hard-codes the 30s default in
   `UpdateService.SendWebhookUpdate`. Add an optional `TimeSpan? timeout` parameter (non-breaking).

> The builtin webhook endpoint (`/api/user/webhooks/builtin`, which goes through message passing) is **not** covered,
> per Q2's wording (`WebhookReceiverService`). If builtin webhooks are the more common integration, consider a
> `webhook.mode = "update" | "builtin"` follow-up.

### 5.3 Schedule (Q3: metadata only)
1. `IScheduleService.GetScheduleByIdAsync(scheduleId)`. Not found → `fail` ("schedule does not exist").
2. The schedule's `AgentName` must match the activation's agent (same ownership rule as `AdminScheduleEndpoints`).
   A mismatch is also reported as not found.
3. `Status == Suspended` → `fail` ("schedule is paused").
4. If `require_last_run_success`: `GetScheduleHistoryAsync(scheduleId, 1)`. If the latest run is `Failed` → `fail`.
   No runs yet → pass, with detail "no runs yet".

The schedule id is validated at create time too, so a typo comes back as `400` instead of a permanently failing check.

## 6. AdminApi

Base: `/api/v{ver}/admin/tenants/{tenantId}/agentActivations/{activationId}/health-checks`
Group setup follows the existing pattern: `RequireAuthorization("AdminEndpointAuthPolicy")` →
`TenantRouteScopeFilter` → `EnforceCapabilities()`. Every route requires `tenant.healthChecks.manage` (R6). Without it
the response is `403`, the same as today's `CapabilityDenied`.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/` | List checks for the activation, with denormalized status |
| `POST` | `/` | Create. `201` with the check, `400` on validation, `409` on a duplicate name. Validates interval ≥ 5, the type-specific payload, and that the activation belongs to the tenant |
| `GET` | `/{checkId}` | Get one |
| `PUT` | `/{checkId}` | Replace definition (`type` immutable), including `enabled` |
| `DELETE` | `/{checkId}` | Delete check, schedule and run history. `204` |
| `GET` | `/{checkId}/runs?from&to&status&page&pageSize` | Paged run history, newest first: `{ items, totalCount, page, pageSize }`. `pageSize` ≤ 100, `status` is a single value |
| `GET` | `/webhook-targets` | The agent's workflows and, for each one running under this activation, its Temporal update methods (read live with the built-in `__temporal_workflow_metadata` query; 5s timeout per workflow). Workflows that aren't running, or whose worker doesn't answer, are listed without methods (`notRunning` / `unavailable`). Feeds the webhook eval form's dropdowns |
| `GET` | `/summary` | Activation health: `healthy` (all pass) / `degraded` (some fail) / `unhealthy` (all fail or error) / `unknown` (no runs), plus per-check last status |

Tenant-wide monitor view (same capability):

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/tenants/{tenantId}/health-checks?status=fail,error&agentName=` | Every check in the tenant with its last status. The "what's broken right now" view for the UI |

Other rules:
- An activation that belongs to a different tenant returns `404`, not `403`, matching `ActivationService`.
- Mutations emit audit and domain events through `DomainEventEmitter`: `HealthCheckCreated`, `HealthCheckUpdated`,
  `HealthCheckDeleted`. **Run results do not emit events.** That's the alerting follow-up's job (Q1).
- With `HealthChecks:Enabled=false`, `POST` and `PUT` return `503`. Reads and deletes keep working.
- API docs come from the endpoint metadata (summaries, descriptions, response types) through Swashbuckle. The
  hand-written `wwwroot/api-docs/*.yaml` files are not served and don't cover activations, so they weren't extended.

### Capability

```csharp
// AdminHealthCheckEndpoints: /tenants/{tenantId}/agentActivations/{activationId}/health-checks
public const string TenantHealthChecksManage = "tenant.healthChecks.manage";

new(TenantHealthChecksManage, [SystemRoles.TenantAdmin],
    "Create, edit, delete, and view automated health checks and their run history."),
```

It defaults to `TenantAdmin` (the issue's persona) and is delegable through the capability matrix like other `tenant.*`
actions. SysAdmin passes through `CapabilityMatrixFilter` unconditionally, as usual.

## 7. Configuration

```jsonc
"HealthChecks": {
  "Enabled": true,
  "TaskQueue": "xians-server-health-checks",
  "MinIntervalMinutes": 5,          // R7 floor; config can raise it, not lower it below 5
  "MaxChecksPerActivation": 20,
  "ReconcileIntervalMinutes": 60
}
```

Run-history retention is not in this section. It uses the TTL index override described in §4.3.

## 8. Risks and mitigations

| Risk | Mitigation |
|---|---|
| **Cost:** every chat check runs the agent (LLM tokens) | 5-min floor, per-activation cap, documented. Usage is attributed to participant `xians-health-check` so it's visible in metrics |
| **Pollution:** health-check messages appear in conversation history and metrics | Dedicated participant id. Follow-up: let admin message/metrics queries exclude it |
| **Side effects:** webhook checks really execute agent logic (create tickets, send email, …) | Documented clearly in the API description. Admins should target an idempotent or test method. Payload is fully admin-controlled |
| Worker not running on any instance → schedules fire but nothing executes | Each run times out after 10 minutes in Temporal, so later runs aren't blocked. A stale `lastRunAt` (much older than the interval) is the visible signal. Surfacing it automatically belongs to the alerting follow-up |
| **Delegation:** runs execute as a tenant-scoped TenantAdmin principal | Fine with the default (only TenantAdmin can configure checks). If `tenant.healthChecks.manage` is ever widened in the capability matrix, the new roles can indirectly send chat messages, invoke webhook update methods and read schedule metadata with TenantAdmin rights within their own tenant. Treat a widening as granting that |
| Shared/template agents whose origin tenant differs from the activation's tenant | The schedule check reads schedules with tenant-scoped TenantAdmin rights. If the agent-permission check rejects that, the run is recorded as `error` rather than wrongly passing |
| Tenant on a dedicated cluster that's unreachable | Executor records `error` with the connection message. Doesn't affect other tenants |
| Platform Temporal unavailable at create time | Check saved as `schedule_state = pending`, reconciler creates the schedule later |

## 9. Hook for the alerting follow-up

The runner's last step (`HealthCheckRunner` → `RecordRunResultAsync`) is where the previous status (`lastStatus` on the
check, read at the start of the run) meets the new one, together with `consecutiveFailures`. This feature only stores
them. The follow-up can detect transitions there (`pass → fail/error`, `fail → pass`, or N consecutive failures) and
publish a `HealthCheckStatusChanged` domain event through `WebhookEventPublisher` / notifications, with no schema change.

## 10. Where the code lives

| Concern | Location |
|---|---|
| Models and request/summary DTOs | `Shared/Data/Models/HealthChecks/` |
| Repository (both collections) | `Shared/Repositories/AgentHealthCheckRepository.cs` |
| Indexes and TTL | `mongodb-indexes.yaml` (`agent_health_checks`, `agent_health_check_runs`) |
| Options | `Shared/Configuration/Options/HealthChecksOptions.cs` |
| Validation, assertions, executors, runner | `Shared/Services/HealthChecks/` |
| Temporal client, workflow, activity, scheduler, reconciler, worker | `Shared/Services/HealthChecks/HealthCheck{Temporal,Scheduler,Reconciler,WorkerService}.cs` |
| CRUD service and activation hooks | `Shared/Services/HealthChecks/AgentHealthCheckService.cs`, called from `ActivationService` |
| Endpoints and registration | `Features/AdminApi/Endpoints/AdminHealthCheckEndpoints.cs`, `AdminApiConfiguration.AddHealthCheckServices` |
| Capability and domain events | `CapabilityActions.TenantHealthChecksManage`, `DomainEventTypes.HealthCheck*` |
| Webhook timeout pass-through | `IWebhookReceiverService.ProcessWebhook(..., timeout, cancellationToken)` (optional parameters, non-breaking) |

Tests (`XiansAi.Server.Tests`):
- Unit (`UnitTests/Shared/Services/HealthChecks/`): validator, assertion evaluator, each executor's pass/fail/error
  mapping, runner skip/record/timeout/exception paths, the system principal, summary derivation, schedule ids and
  jitter, reconciler.
- Integration (`AdminHealthCheckEndpointsTests`, Mongo with an in-memory fake scheduler): CRUD, validation, duplicate
  name, `403` without the capability, cross-tenant `404`, deactivate pauses, activation delete removes checks, runs
  paging/filtering, summary, tenant-wide filter.
- Temporal (`AdminApiTemporalHealthCheckSchedulerTests`, local Temporal dev server): the real scheduler (create, update,
  pause, list, idempotent delete), plus end to end: a schedule check created over the API is triggered in Temporal, run by
  the server's own worker, and recorded as `pass`.

## 11. Review decisions

| # | Question | Decision |
|---|---|---|
| D1 | Host all tenants' health-check schedules in the platform Temporal namespace, worker in the server process (§3.1) | **Yes** |
| D2 | Optional `expected_status_code` assertion on webhook checks (§5.2) | **Yes** |
| D3 | Builtin webhooks (`/webhooks/builtin`) out of scope for v1 (§5.2) | **Yes, out of scope** |
| D4 | Default role for `tenant.healthChecks.manage` | **`TenantAdmin` only** |
| D5 | Run retention | **30 days by default, configurable** through the TTL index override (§4.3) |
