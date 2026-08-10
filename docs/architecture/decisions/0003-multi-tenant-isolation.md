# ADR-0003: Multi-Tenant Isolation Enforcement

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server is a multi-tenant SaaS platform. Each tenant's data (agents, workflows, conversations, secrets) must be strictly isolated to prevent unauthorized cross-tenant access (IDOR vulnerabilities). Without enforced isolation, a compromised API key or token could leak data across tenant boundaries.

We needed a mechanism to:
- Ensure route-level tenant parameters match the authenticated tenant
- Fail fast on unauthorized tenant access attempts
- Apply isolation consistently across all tenant-scoped endpoints

## Decision

We enforce multi-tenant isolation using **endpoint filters** applied to route groups:

1. All tenant-scoped routes include `{tenantId}` as a route parameter
2. A `TenantRouteScopeFilter` (or equivalent) validates that the authenticated user/API key has access to the requested `tenantId`
3. The filter is applied at the route group level:
   ```csharp
   var group = adminApiGroup.MapGroup("/tenants/{tenantId}")
       .AddEndpointFilter<TenantRouteScopeFilter>();
   ```
4. Unauthorized access attempts result in HTTP 403 Forbidden

Repository and service layers receive the validated `tenantId` and use it in all queries.

## Consequences

**Positive:**
- Centralized enforcement prevents developers from forgetting tenant checks
- Fail-fast behavior reduces risk of accidental data leaks
- Clear audit trail: 403 responses indicate authorization failures
- Simplified endpoint logic: tenantId is pre-validated

**Negative:**
- Adds overhead to every tenant-scoped request (filter execution)
- Requires consistent tenantId naming in routes
- Cross-tenant operations (rare) require special handling

**Mitigations:**
- Filter logic is lightweight and cached where possible
- Document cross-tenant use cases (e.g., system-level operations) and their authorization model
- Use integration tests to verify isolation enforcement
