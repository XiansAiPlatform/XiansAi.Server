# Token Limiting Implementation Plan

## 1. Goals & Scope
- Enforce configurable token consumption limits per tenant and per user (user override inherits from tenant).
- Short-circuit LLM operations (REST, SSE, Workflow/Temporal runs, Markdown generation, etc.) once a user exceeds the configured limit.
- Provide administrators and workers with tooling to inspect usage, configure quotas, and surface “usage exceeded” notifications to front-end clients.
- Capture actual token consumption for observability, reporting, and enforcement audit logs.

## 2. High-Level Architecture
```
┌──────────────┐        HTTPS         ┌───────────────────────┐
│Temporal Flow │  ->  /api/agent/...  │ XiansAi.Server (API)  │
│(XiansAi.Lib) │ <--------------------│ - TokenUsageService   │
└──────────────┘   usage data/checks  │ - Mongo repositories  │
        │                              └───────────────────────┘
        │ token usage
        ▼
┌─────────────────┐
│Mongo Collections │ (token limits, rolling windows, audit logs)
└─────────────────┘
```

## 3. Data Model & Persistence
1. **Collections**
   - `token_usage_limits`
     - Keys: `tenantId`, `userId?`, `windowSeconds`, `maxTokens`, `effectiveFrom`, `enabled`
     - Index: `{ tenantId: 1, userId: 1 }`
   - `token_usage_windows`
     - Keys: `tenantId`, `userId`, `windowStart`, `windowSeconds`, `tokensUsed`, `updatedAt`
     - Index: `{ tenantId: 1, userId: 1, windowStart: -1 }`
   - `token_usage_events`
     - Audit log per LLM call (tenant/user, model, promptTokens, completionTokens, workflowId/threadId)
2. **Repositories (Shared/Repositories)**
   - `ITokenUsageLimitRepository` for CRUD on limits.
   - `ITokenUsageWindowRepository` for atomic `$inc`/upsert window counters.
   - `ITokenUsageEventRepository` for insert-only audit records.
3. **Seed/Migration**
   - Optional script/seed file to set default tenant limits.

## 4. Configuration
- `appsettings.*`: add `TokenUsage` section with defaults:
  ```json
  "TokenUsage": {
    "Enabled": true,
    "DefaultTenantLimit": 200000,
    "WindowSeconds": 86400,
    "WarningPercentage": 0.8
  }
  ```
- Bind to `TokenUsageOptions` class; register via options pattern in `Shared/Configuration/SharedServices.cs`.

## 5. Core Services
1. **`ITokenUsageService`**
   - Methods:
     - `Task<TokenUsageStatus> CheckAsync(string tenantId, string userId)`
     - `Task RecordAsync(TokenUsageRecord record)`
   - Responsibilities:
     - Resolve effective limit (user > tenant > defaults).
     - Query/initialize rolling window; determine remaining tokens.
     - Throw `TokenLimitExceededException` when limit reached.
     - Emit warning events at threshold (80%).
2. **`TokenUsageStatus` DTO**
   - `MaxTokens`, `TokensUsed`, `TokensRemaining`, `WindowEndsAt`, `IsExceeded`.
3. **`TokenUsageRecord` DTO**
   - Contains `tenantId`, `userId`, `model`, `promptTokens`, `completionTokens`, `workflowId`, `requestId`, `source` (MarkdownService, SemanticRouter, etc.).
4. **Registration**
   - Add repositories + service registrations in `Shared/Configuration/SharedServices.cs`.

## 6. API Surface (Server)
1. **Tenant/User Management (Web API)**
   - New endpoints under `Features/WebApi/Endpoints/TenantUsageEndpoints.cs`:
     - `GET /api/client/usage/limits?tenantId&userId`
     - `POST/PUT /api/client/usage/limits` (create/update limit records).
     - `DELETE /api/client/usage/limits/{id}`.
     - Authorization requires owner/admin roles.
2. **Agent API (used by workers)**
   - Add `TokenUsageEndpoints` under `Features/AgentApi/Endpoints/`.
     - `GET /api/agent/usage/status` (returns current `TokenUsageStatus` for the certificate tenant+user).
     - `POST /api/agent/usage/report` to submit actual usage (`TokenUsageRecord`).
   - Protected via `RequiresCertificate`.

## 7. Enforcement Points (Server)
1. **User-Facing entry points**
   - `MessageService.ProcessIncomingMessage`: call `ITokenUsageService.CheckAsync` before `SaveMessage`. On exceed, return `ServiceResult<string>.Forbidden("TOKEN_USAGE_EXCEEDED")`.
   - Webhook/Websocket endpoints (under `Features/UserApi/WebhookEndpoints`, `SocketEndpoints`): same pre-check before `messageService.ProcessIncomingMessage`.
2. **Server-side LLM consumption**
   - `MarkdownService.GenerateMarkdown`: check+record tokens around `_llmService.GetChatCompletionAsync`.
   - `CertificateService.GetFlowServerSettings`: may not need tokens, but include remaining quota in response for worker awareness.
3. **Response handling**
   - When `TokenLimitExceededException` thrown, translate to HTTP 403 with JSON body (`{ errorCode, message, tokensRemaining }`).

## 8. LLM Provider & Service Updates
1. **`ILlmService`**
   - Wrap `GetChatCompletionAsync` to accept `LlmInvocationContext` (tenant/user/workflow).
   - Internal flow:
     1. `TokenUsageService.CheckAsync`.
     2. Call provider.
     3. Parse usage metadata (`completion.Value.Usage` for OpenAI; `usage.total_tokens` for Azure; placeholder for Anthropic).
     4. `TokenUsageService.RecordAsync`.
2. **Provider Implementations**
   - `OpenAILlmProvider`: use `ChatClientResponse.Usage` to capture prompt/output tokens.
   - `AzureOpenAILlmProvider`: parse JSON `usage` block.
   - `Dummy/Anthropic`: return synthetic usage counts (based on message length) until API data available.

## 9. XiansAi.Lib (Temporal Worker) Changes
1. **New client**
   - `Server/TokenUsageClient.cs` that hits `/api/agent/usage/*` via `SecureApi`.
2. **Semantic Router Guard**
   - `SemanticRouterHubImpl.RouteAsync` & `CompletionAsync`:
     - Before building history, call `TokenUsageClient.EnsureWithinLimitAsync`.
     - After `agent.InvokeAsync`, accumulate tokens:
       - Prefer `ChatMessageContent.Metadata` usage info if connectors expose it.
       - Fallback to `ChatHistoryReducer.EstimateTokenCount`.
     - Report via `TokenUsageClient.ReportAsync`.
3. **ChatHandler Integration**
   - In `ProcessMessage`, catch `TokenLimitExceededException`:
     - Send `messageThread.SendChat` with friendly “Usage exceeded” text and `messageThread.SendData` containing structured payload.
4. **SystemActivities**
   - `RouteAsync` and `CompletionAsync` should re-use the same guard to cover workflows calling activities directly.
5. **Context propagation**
   - Ensure `AgentContext` exposes `TenantId`/`UserId` to `TokenUsageClient`.

## 10. Front-End / Client Feedback
1. **REST responses**
   - When `ProcessIncomingMessage` returns forbidden, `RestEndpoints` should propagate HTTP 403 with JSON body (error code + remaining tokens).
2. **Conversation Stream**
   - When worker sends the “usage exceeded” chat/data message, SSE/webhooks automatically deliver to UI.
3. **Admin UI**
   - Extend documentation/API reference (docs/user-api, docs/webapi) to describe the new responses and configuration steps.

## 11. Telemetry & Monitoring (Optional)
- If the OpenTelemetry helpers are reintroduced, extend `RecordTokenUsage` with tags like
  `token.limit`, `token.remaining`, and `token.source`.
- Regardless of OTEL, emit warning logs when users cross the 80% threshold and capture basic
  metrics via the existing logging/monitoring stack.
- Skip this step entirely while the telemetry extensions remain reverted.

## 12. Testing Strategy
1. **Unit Tests**
   - `TokenUsageServiceTests`: limit resolution, window increments, exceeded conditions.
   - `MessageServiceTests`: ensures usage check invoked, forbids processing when exceeded.
   - `SemanticRouterHubTests`: verifies guard prevents LLM invocation in lib.
2. **Integration Tests (Server)**
   - Under `XiansAi.Server.Tests/IntegrationTests/UserApi`: simulate hitting limit and expect 403.
   - Under `.../AgentApi`: test `/usage/status` & `/usage/report`.
3. **End-to-End (Lib + Server)**
   - Temporal workflow test harness that mocks server endpoints to ensure proper call order.

## 13. Deployment & Rollout
1. **Feature flag**
   - `TokenUsage.Enabled` default true but can be toggled per environment.
2. **Backfill**
   - Run script to create tenant defaults before deploying enforcement.
3. **Monitoring**
   - Dashboards for usage per tenant; alerts when frequent limit hits occur.
4. **Docs**
   - Add `docs/TOKEN_LIMITING_IMPLEMENTATION.md` for end-user/ops guidance (separate from this plan).

## 14. Open Questions / Follow-Ups
- How should multi-tenant shared users (e.g., Support staff) aggregate limits? (Current plan: per `(tenantId, userId)` pair.)
- Do we need grace tokens or soft limits? Consider `WarningPercentage` customizing per tenant.
- Should token usage events be exposed to clients (e.g., `/api/client/usage/history`)? Not in scope now but schema supports it.

## 15. XiansAi.UI (Manager Portal) Updates
1. **Usage API Hook**
   - Add `useUsageApi` (`src/modules/Manager/services/usage-api.js`) mirroring other API hooks. Methods include
     `getTenantUsageStatus`, `getTenantLimit`, `saveTenantLimit`, `listUserOverrides`, `upsertUserOverride`,
     `deleteUserOverride`, and (optionally) `getUsageHistory`.
2. **System Admin View (`/manager/admin`)**
   - Extend `AdminDashboard.jsx` with a new tab (“Usage Limits”) that lists all tenants. Each row shows the configured
     max tokens, current usage/remaining tokens, and window reset time.
   - Provide actions such as “Edit Limit” (opens slider/drawer with `LimitFormFields`) and “View Overrides”
     (modal listing per-user limits). Implement supporting components (`TokenUsageManagement`, `TenantUsageCard`,
     `LimitDrawer`) under `Components/Admin/`.
3. **Tenant Admin View (`/manager/settings`)**
   - Add a “Usage” tab (only when `useTenant().isAdmin` is true). Inside:
     - Show current tenant status (tokens used, remaining, reset timestamp).
     - Allow editing the tenant default limit.
     - Display user-specific overrides within the tenant with add/edit/delete controls.
   - Implement in a new `TenantUsage.jsx`, reusing shared UI pieces.
4. **Shared UI Elements**
   - Create reusable components under `Components/Common/` such as `UsageProgressBar`, `LimitFormFields`,
     and `UsageStatsCard` so both admin and tenant pages stay consistent.
5. **Docs & Navigation**
   - Ensure navigation tabs render only for the correct roles (`isSysAdmin` for admin tab, `isAdmin` for tenant tab).
   - Update front-end docs (`docs/nonfunctional/app-structure.md` or similar) to describe where Usage management lives
     and what permissions are required.

---
**Next Action:** Implement repositories + service scaffolding (Section 3–5) before hooking into entry points.

