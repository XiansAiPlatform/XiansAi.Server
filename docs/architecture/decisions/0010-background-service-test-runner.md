# ADR-0010: Test Runner is a BackgroundService, not a Temporal Schedule

**Status:** Proposed

**Date:** 2026-09-29

**Related:** #517, R4, R7

## Context

R4 of #517 requires test runs to execute periodically. The issue text suggests "using Temporal workflows in the server," and the platform can create Temporal schedules (`Features/Mcp/Tools/ScheduleTools.cs` → `client.CreateScheduleAsync`).

However, the probes themselves exercise Temporal: the chat probe sends a workflow signal, the webhook probe sends a workflow update, and the schedule probe describes a Temporal schedule. A Temporal-scheduled runner that probes Temporal would **not fire when Temporal is down** — masking exactly the failures these tests exist to detect. This is the self-referential risk flagged during grooming (Q3 notes).

## Decision

Implement the runner as a `Shared/` `BackgroundService` (`AgentTestRunnerService`), following the existing `ExpiredMessageFileCleanupService` precedent. It polls `agent_tests` for due runs (~1 min cadence), enforces the per-test interval with a server-side 5-minute minimum (R7) in-process, and processes each test in its own try/catch so one failing probe never aborts the sweep.

## Consequences

- Runner liveness is independent of Temporal; Temporal outages surface as `Failed`/`Error` runs instead of silent non-execution.
- No test-schedule namespace to clean up on deactivation, simplifying R8 (the lifecycle service just flips `enabled=false`).
- A distributed-execution guard is required if the server runs multiple replicas (per-test `findOneAndUpdate` claim on `last_run_at`) — tracked as open decision D3.
