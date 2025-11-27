# Token Limiting Delivery Phases

This checklist breaks the implementation plan into sequential phases so we can land and validate the feature incrementally. Each phase should land behind configuration toggles where possible to keep rollout safe.

---

## Phase 1 – Data & Service Foundations
- [ ] Define Mongo models for `token_usage_limits`, `token_usage_windows`, `token_usage_events`.
- [ ] Implement repositories (`ITokenUsageLimitRepository`, `ITokenUsageWindowRepository`, `ITokenUsageEventRepository`) with unit tests for the `$inc` / upsert behavior.
- [ ] Add `TokenUsageOptions` configuration binding + defaults (`appsettings.*`).
- [ ] Implement `TokenUsageService` (`CheckAsync`, `RecordAsync`, limit resolution) with unit tests.
- [ ] Register repositories + service in `SharedServices`.
- [ ] (Optional) Add seed script / migration docs for default tenant limits.

## Phase 2 – Server API Surface
- [ ] Expose tenant/admin endpoints under `/api/client/usage/*`:
  - GET status, GET/POST/PUT/DELETE limit records, GET usage history (optional).
- [ ] Add Agent API endpoints `/api/agent/usage/status` and `/api/agent/usage/report`.
- [ ] Write integration tests for both API groups (entitlements, validation, happy paths).
- [ ] Update docs (`docs/user-api`, `docs/webapi`) with the new endpoints.

## Phase 3 – Enforcement Hooks
- [ ] Guard `MessageService`, webhook, websocket entry points with `TokenUsageService.CheckAsync`.
- [ ] Wrap `ILlmService` / provider calls to capture usage metadata and record via `RecordAsync`.
- [ ] Build `TokenUsageClient` in `XiansAi.Lib` and enforce checks inside `SemanticRouterHubImpl`, `ChatHandler`, and `SystemActivities`.
- [ ] Ensure workflows send user-facing “usage exceeded” messages and propagate HTTP 403 responses.
- [ ] Add integration/functional tests proving requests are blocked once limits are exceeded.

## Phase 4 – UI Management Features
- [ ] Add `useUsageApi` hook in `XiansAi.UI`.
- [ ] System admin view (`/manager/admin`) gets a “Usage Limits” tab listing tenants, edit drawers, and override modals.
- [ ] Tenant admin view (`/manager/settings`) gets a “Usage” tab for scoped stats + overrides.
- [ ] Create shared UI elements (`UsageProgressBar`, `LimitFormFields`, etc.) and documentation updates.
- [ ] Hook UI validation/errors into existing notification + loading systems.

## Phase 5 – Telemetry, Rollout, Docs
- [ ] (Optional) Reintroduce OpenTelemetry token metrics if/when telemetry extensions return.
- [ ] Create dashboards/alerts for token usage thresholds.
- [ ] Update operational docs (`docs/TOKEN_LIMITING_IMPLEMENTATION.md`, runbooks) and announce feature flag strategy.
- [ ] Plan rollout: seed default limits, enable feature flag per environment, monitor, then enforce globally.

---
Use this checklist to track progress across teams. Update it as requirements evolve.

