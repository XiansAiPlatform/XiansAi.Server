# ADR-0010: Test Runner is a BackgroundService, not a Temporal Schedule

**Status:** Proposed

**Date:** 2026-09-29

**Related:** #517, R4, R7

## Context

R4 of #517 requires test runs to execute periodically. The issue text suggests "using Temporal workflows in the server," and the platform can create Temporal schedules (`Features/Mcp/Tools/ScheduleTools.cs` → `client.CreateScheduleAsync`).

However, the probes themselves exercise Temporal: the chat probe sends a workflow signal, the webhook probe sends a workflow update, and the schedule probe describes a Temporal schedule. A Temporal-scheduled runner that probes Temporal would **not fire when Temporal is down** — masking exactly the failures these tests exist to detect. This is the self-referential risk flagged during grooming (Q3 notes).

## Decision

Implement the runner as a `Shared/` `BackgroundService` (`AgentTestRunnerService`), following the existing `ExpiredMessageFileCleanupService` precedent. It polls `agent_tests` for due runs (~1 min cadence), enforces the per-test interval with a server-side 5-minute minimum (R7) in-process, and processes each test in its own try/catch so one failing probe never aborts the sweep.

Because the server runs multiple replicas (D3 confirmed, #517), the runner does **not** use a plain due-scan — two replicas would probe the same test in the same window. Instead each due test is claimed atomically via `AgentTestRepository.ClaimNextDueAsync`, a single `findOneAndUpdate` that matches an enabled, due test and sets `last_run_at = now` in the same operation, returning the claimed document. Mongo's per-document atomicity guarantees only one replica wins each test per interval; the runner loops on the claim until it returns null, then sleeps.

## Consequences

- Runner liveness is independent of Temporal; Temporal outages surface as `Failed`/`Error` runs instead of silent non-execution.
- No test-schedule namespace to clean up on deactivation, simplifying R8 (the lifecycle service just flips `enabled=false`).
- Multi-replica execution is safe by construction via the atomic `ClaimNextDueAsync` claim (D3 resolved — server is multi-replica; the guard ships in v1, not deferred).
- Residual: a replica crashing mid-probe leaves the test claimed until its next interval (it simply runs one cycle later). Acceptable for v1; a short claim lease can tighten this later if needed.
