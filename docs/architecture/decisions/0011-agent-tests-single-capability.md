# ADR-0011: Single Capability Gates All Agent-Test Operations

**Status:** Proposed

**Date:** 2026-09-29

**Related:** #517, R6, ADR-0005 (Authentication)

## Context

R6 of #517 requires access to create / edit / view automated tests to be gated by **one** new capability. Elsewhere `CapabilityActions` sometimes uses finer, per-operation granularity (separate read vs write actions).

## Decision

Add a single action `tenant.agentTests.manage` (`CapabilityActions.TenantAgentTestsManage`), with `DefaultRoles = [TenantAdmin]` and delegable (not `NonDelegable`), applied via `.RequireCapability(...)` to every route in the agent-tests endpoint group.

## Consequences

- Matches R6 exactly with the smallest capability-matrix footprint.
- Read access is not separable from write; if read-only viewing is later required, a second action can be added without breaking existing grants.
- Default-role and delegability choice is confirmable by a human (open decision D1); the recommendation mirrors other tenant-scoped management actions.
