# ADR-0006: URL-Based API Versioning

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server provides public APIs consumed by external clients (web apps, mobile apps, integrations). Over time, breaking changes to the API contract are inevitable. We needed a versioning strategy that:

- Allows multiple API versions to coexist
- Provides clear, discoverable version information
- Supports client caching and routing
- Avoids complex header or query parameter schemes

## Decision

We adopt **URL-based API versioning** with the version segment embedded in the path:

```
/api/v1/admin/tenants
/api/v2/admin/tenants
```

All public API routes include a version segment (`/api/v{version}/`). Internal or webhook endpoints that are not part of the public contract may omit versioning.

Version increments:
- **Major version** (`v1` → `v2`): Breaking changes (removed fields, changed semantics)
- **Minor/patch versions**: Not exposed in the URL; clients always get the latest minor/patch for their major version

Route groups are versioned at registration:
```csharp
var v1Group = app.MapGroup("/api/v1/admin");
v1Group.MapAdminEndpoints(); // v1 endpoints

var v2Group = app.MapGroup("/api/v2/admin");
v2Group.MapAdminEndpointsV2(); // v2 endpoints with breaking changes
```

## Consequences

**Positive:**
- Explicit and discoverable: version is visible in the URL
- Cache-friendly: different URLs = different cache keys
- Simple routing: no need to inspect headers or query parameters
- Client clarity: developers know exactly which version they're using
- Supports gradual migration: old clients use v1, new clients use v2

**Negative:**
- URL length increases slightly
- Endpoint proliferation if not managed (v1, v2, v3 variants)
- Requires discipline to maintain multiple versions concurrently

**Mitigations:**
- Deprecate and remove old versions after a transition period
- Document migration paths for breaking changes
- Use shared logic for non-breaking differences (version-specific DTOs, shared services)
- See `Features/AdminApi/API_VERSIONING_GUIDE.md` for implementation guidance
