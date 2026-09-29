# ADR-0009: Schedule Metadata Probe as an Independent Read-Only Shared Service

**Status:** Proposed

**Date:** 2026-09-29

**Related:** #517, R3, ADR-0001 (Feature Slice Architecture), ARCH-003, ARCH-004

## Context

R3 of #517 defines the schedule test as a **metadata check** — schedule exists, is enabled, has a recent run — via Temporal `DescribeAsync()` (`state.Paused`, `Info.RecentActions`), not a synthetic execution.

Equivalent describe-and-classify logic already exists in `Features/WebApi/Services/ScheduleService.cs`, but that class is a ~1200-line file with many `DescribeAsync()` / `state.Paused` / `RecentActions` call sites serving unrelated read/write operations. Two options: reuse/refactor `ScheduleService` (pulling shared logic out), or build a small independent read-only probe.

The test runner is a `Shared/` `BackgroundService` (ADR-0010). Reusing `ScheduleService` (in `Features/WebApi`) from `Shared/` would also violate ARCH-003.

## Decision

Add a small, read-only `Shared/Services/ScheduleMetadataProbe.cs` (`IScheduleMetadataProbe`) that performs its **own** `DescribeAsync()` via `ITemporalGatewayFactory` and returns exists / enabled / last-run status. It does **not** refactor or depend on `ScheduleService`, which is left untouched.

## Consequences

- The runner reads schedule metadata using only Shared Temporal primitives — no reference to `Features/WebApi`; ARCH-003/004 respected with no deviation.
- The 1200-line `ScheduleService` hot file is not touched, avoiding regression risk to unrelated schedule operations.
- The classification rule (paused → disabled, empty `RecentActions` → no last run) is duplicated in the probe; it is a few lines and read-only, so the duplication cost is low. If the rule later grows, it can be promoted to a shared helper.
