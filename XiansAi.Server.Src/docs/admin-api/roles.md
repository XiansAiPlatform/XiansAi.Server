# Admin API Roles & Permissions

This document describes the roles recognized by the Admin API (`/api/{version}/admin/...`)
and the permissions each role grants.

## Overview

Every Admin API request is authenticated with an API key (`sk-Xnai-...`) passed in the
`Authorization: Bearer` header, and every endpoint is protected by the
`AdminEndpointAuthPolicy` authorization policy.

Authentication and authorization happen in two stages:

1. **`AdminEndpointAuthenticationHandler`** validates the API key, looks up the key owner's
   roles, and resolves the target tenant.
2. **`ValidAdminEndpointAccessHandler`** (the policy requirement) confirms the resolved
   context carries an admin role.

Both stages delegate role/tenant resolution to **`AdminRoleTenantResolver`**, which is the
single source of truth for who may call the Admin API.

> **Key rule:** Only callers whose API key owner holds the **`SysAdmin`** or **`TenantAdmin`**
> role can use the Admin API. Access is granted by an *explicit* role assignment only —
> email-domain matching is intentionally **not** used to grant admin access.

## System Roles

All roles are defined in `Shared/Auth/SystemRoles.cs`:

| Role | Constant | Admin API caller? |
|------|----------|-------------------|
| System Administrator | `SysAdmin` | **Yes** |
| Tenant Administrator | `TenantAdmin` | **Yes** |
| Tenant Participant Admin | `TenantParticipantAdmin` | No |
| Tenant Participant | `TenantParticipant` | No |
| Tenant User | `TenantUser` | No |

Only the first two grant access to the Admin API. The remaining three are roles that admins
*assign to and manage on* users through the Admin API; on their own they cannot call any
Admin API endpoint.

> **"No" means "not an Admin API caller" — not "unimportant".** `TenantParticipantAdmin`,
> `TenantParticipant`, and `TenantUser` are first-class, **backend-enforced role constants**,
> not free-form strings. The platform branches on their exact values in several places, so
> they must always be referenced via the `SystemRoles` constants (never string literals):
>
> - **WebApi authorization policies** — e.g. `WebApiConfiguration` / `AuthConfigurationExtensions`
>   call `RequireRole(SysAdmin, TenantAdmin, TenantUser)`.
> - **Effective-role computation** — `RoleCacheService` deliberately excludes
>   `TenantParticipant` and `TenantParticipantAdmin` when resolving admin roles.
> - **Privilege ordering & normalization** — `TenantParticipantUserService` and
>   `AdminParticipantsEndpoints` rank roles `TenantAdmin > TenantUser > TenantParticipantAdmin
>   > TenantParticipant`.
> - **Repository queries** — `UserRepository` filters tenant memberships by these role values.
> - **Defaults** — every auth provider (Keycloak, OIDC, AzureB2C, GitHub) assigns
>   `TenantUser` as the default role when no tenant is supplied.
> - **Validation allow-lists** — `UserTenantService` validates an incoming role against
>   `{ TenantAdmin, TenantUser, TenantParticipant, TenantParticipantAdmin }`.
>
> They are stored as `const string` (not an `enum`) on purpose: the values are persisted in
> tenant role lists and emitted as `ClaimTypes.Role` claims, so they must serialize cleanly to
> the database and to JWTs without an enum↔string mapping layer.

## Roles That Grant Admin API Access

### `SysAdmin` — System Administrator

- **Tenant scope: any tenant.** A SysAdmin may target any tenant by supplying a `tenantId`
  (resolved from query string, route value, or the `X-Tenant-Id` header, in that priority
  order). `AdminRoleTenantResolver` validates that the tenant exists and locks the request
  context to it. If no `tenantId` is supplied, the SysAdmin's own API-key tenant is used.
- **Inherits all `TenantAdmin` permissions**, scoped to whichever tenant is targeted.
- **Exclusive (SysAdmin-only) operations** — these are gated with
  `AdminTenantScopeGuard.IsSysAdmin(...)` or a direct `SysAdmin` role check and return
  `403 Forbidden` for any other role:
  - **Tenant lifecycle** (`AdminTenantEndpoints`): list, get, create, update, delete tenants.
  - **Global user management** (`AdminGlobalUserEndpoints`): list/get/patch users
    platform-wide, set the SysAdmin flag, and set user status.
  - **Cross-tenant participant lookup** (`AdminParticipantsEndpoints`): look up a participant
    and their tenant memberships across all tenants.
  - **Cross-tenant template operations** (`AdminTemplateEndpoints`): the create/update/delete
    and cross-tenant read paths.
  - **Ownership-based resources** (e.g. knowledge): can modify any resource regardless of
    ownership.

### `TenantAdmin` — Tenant Administrator

- **Tenant scope: own tenant only.** A TenantAdmin is locked to the tenant of their API key.
  If they pass a `tenantId` that does not match their key's tenant, the request is rejected
  (`"Tenant ID does not match API key tenant"`).
- **Permissions within their own tenant** include managing:
  - Tenant participant users (`AdminUserEndpoints`)
  - Agents, agent deployment, and activation
  - Knowledge entries
  - Schedules, tasks, and workflows
  - API keys and secret vault entries
  - Messaging, app integrations, logs, metrics, and stats
- **Cannot** perform any of the SysAdmin-only operations listed above (e.g. creating or
  deleting tenants, global user management, or cross-tenant participant lookup).

## Tenant Scope Enforcement (IDOR Protection)

Because the tenant used during authentication can come from a query string or header while
endpoint handlers bind `tenantId` from the route path, a caller could otherwise authenticate
against their own tenant while pointing the route at a different (victim) tenant.

To close this cross-tenant IDOR vector, routes nested under `/tenants/{tenantId}` apply the
**`TenantRouteScopeFilter`** endpoint filter (backed by
`AdminTenantScopeGuard.RouteMatchesContext`). It rejects any request whose route `tenantId`
does not match the authoritative resolved context tenant, returning:

```json
{ "message": "Tenant scope mismatch" }
```

with HTTP `403 Forbidden`.

For workflow-scoped routes, `AdminTenantScopeGuard.WorkflowIdBelongsToContext` performs the
equivalent check by verifying the tenant segment of the workflow ID
(`{tenantId}:{agent}:{workflowType}[:{activation}]`).

## Ownership-Based Permissions

For resources that track ownership (such as knowledge tied to a deployed agent), there is a
permission tier *below* admin. The effective check (see
`AdminKnowledgeEndpoints.CanModifyAgentResource`) is:

| Caller | Can modify |
|--------|-----------|
| `SysAdmin` | Any resource in any tenant |
| `TenantAdmin` | All resources in their tenant |
| Resource owner | Only resources they own (`OwnerAccess` contains their user ID) |

## Roles Assigned to Managed Users

When an admin creates or updates a tenant user via `AdminUserEndpoints`, the `role` field may
be any tenant role:

- `TenantAdmin`
- `TenantUser`
- `TenantParticipant`
- `TenantParticipantAdmin`

`TenantParticipant` is the default role assigned to a non-SysAdmin user. Assigning these roles
does **not** grant the user Admin API access unless the assigned role is `TenantAdmin` (or the
user is flagged as `SysAdmin`).

## Failure Responses

| Scenario | Result |
|----------|--------|
| Missing / malformed API key (not `sk-Xnai-...`) | `401 Unauthorized` |
| Valid key, but owner lacks `SysAdmin`/`TenantAdmin` | `401 Unauthorized` (`"User does not have required admin role"`) |
| TenantAdmin targets a different tenant than their key | `401 Unauthorized` (`"Tenant ID does not match API key tenant"`) |
| SysAdmin targets a non-existent tenant | `404 Not Found` (`TenantNotFoundException`) |
| Route `tenantId` does not match resolved context tenant | `403 Forbidden` (`"Tenant scope mismatch"`) |
| Authenticated admin calls a SysAdmin-only endpoint without `SysAdmin` | `403 Forbidden` |

## Related Code

- `Shared/Auth/SystemRoles.cs` — role constants
- `Features/AdminApi/Auth/AdminRoleTenantResolver.cs` — role & tenant resolution
- `Features/AdminApi/Auth/AdminEndpointAuthenticationHandler.cs` — API key authentication
- `Features/AdminApi/Auth/ValidAdminEndpointAccessHandler.cs` — authorization requirement
- `Features/AdminApi/Auth/AdminTenantScopeGuard.cs` — tenant-scope guards & route filter
