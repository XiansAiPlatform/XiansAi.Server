# ADR-0012: Store Test Definitions in a Separate Collection

**Status:** Proposed

**Date:** 2026-09-29

**Related:** #517, R1, ADR-0004 (Repository Pattern)

## Context

R1 of #517 attaches tests to an `AgentActivation`. Two shapes are possible: embed a tests array inside the `AgentActivation` document, or store tests in their own collection keyed by activation id.

`AgentActivation.SanitizeAndReturn()` whitelists fields, so an embedded array would be silently dropped on sanitize/update. Run results also grow unbounded and would bloat the activation document if embedded.

## Decision

Store `AgentTest` (definitions) and `AgentTestRun` (results) in separate MongoDB collections (`agent_tests`, `agent_test_runs`), each keyed by `tenant_id` + `activation_id` (with `agent_name` cached). `AgentActivation` is unchanged. Access is via dedicated repositories (`AgentTestRepository`, `AgentTestRunRepository`) per ADR-0004 / ARCH-007.

## Consequences

- Test definitions survive activation sanitize; run history grows independently with its own indexes and optional TTL.
- Referential integrity between tests and their activation is enforced in code — the lifecycle service disables tests on deactivate/delete (R8) — rather than by a document boundary.
- One more collection pair to index and back up.
