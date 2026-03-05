# Secret Vault Scope Validation (Server)

This document describes the **scope validation** applied to Secret Vault operations on the server. It ensures that callers can only perform CRUD on secrets within the tenants they are allowed to access, based on their role (SysAdmin vs TenantAdmin) and certificate/context.

## Overview

- **SysAdmin**: May perform Secret Vault CRUD on **any** tenant. Request scope (tenantId, agentId, userId, activationName) is used as-is.
- **TenantAdmin** (or any caller without SysAdmin): May perform Secret Vault CRUD **only** within the tenant from their certificate or auth context. The effective tenant is forced to `tenantContext.TenantId`; if the request specifies a different tenant, the server returns **403 Forbidden**.

This applies to both:

- **Agent API** (`/api/agent/secrets`) – certificate auth; roles and tenant come from the certificate/context.
- **Admin API** (`/api/v1/admin/secrets`) – API key / admin auth; roles and tenant come from the logged-in user context.

## Implementation

### Shared enforcement: `SecretVaultScopeEnforcement`

Location: **`Shared/Auth/SecretVaultScopeEnforcement.cs`**

A static helper used by both Agent and Admin Secret Vault endpoints.

#### 1. `TryResolveScope` (create, list, fetch, update body)

Used for operations that accept scope in the request (create, list, fetch, and the update body).

| Input | Description |
|-------|-------------|
| `tenantContext` | Current auth context (`ITenantContext`: TenantId, UserRoles, LoggedInUser). |
| `requestTenantId`, `requestAgentId`, `requestUserId`, `requestActivationName` | Scope values from the request body or query. |

| Behaviour | SysAdmin | Non–SysAdmin (e.g. TenantAdmin) |
|-----------|----------|----------------------------------|
| **Effective tenant** | Request `tenantId` as-is (any tenant allowed). | Forced to `tenantContext.TenantId`. |
| **Request sends a different tenantId** | Allowed. | **403 Forbidden**: *"Access denied. Secret Vault operations are only allowed within your tenant."* |
| **Context has no tenant** | N/A. | **403 Forbidden**: *"Tenant scope is required. Certificate or user context has no tenant."* |
| **AgentId, UserId, ActivationName** | Passed through from request. | Passed through from request; only tenant is restricted. |

Returns:

- `true` and the effective scope (effectiveTenantId, effectiveAgentId, effectiveUserId, effectiveActivationName) when the request is allowed.
- `false` and a `ServiceResult<object>.Forbidden` when the request must be rejected with 403.

#### 2. `CanAccessSecretTenant` (get by id, update, delete)

Used for operations that identify a secret by **id** (get by id, update, delete). The server loads the secret first, then checks whether the caller may access that secret’s tenant.

| Input | Description |
|-------|-------------|
| `tenantContext` | Current auth context. |
| `secretTenantId` | The `TenantId` of the secret (from the loaded document). |

| Behaviour | SysAdmin | Non–SysAdmin |
|-----------|----------|---------------|
| **Any secret** | Allowed (including cross-tenant, i.e. `secretTenantId == null`). | Not allowed for other tenants. |
| **Secret in context tenant** | Allowed. | Allowed only if `secretTenantId == tenantContext.TenantId`. |
| **Cross-tenant secret** (`secretTenantId == null`) | Allowed. | **Not** allowed (treated as SysAdmin-only). |

Returns `true` if the caller may access the secret, `false` otherwise. The endpoint then returns **403 Forbidden** with *"Access denied. Secret is not in your tenant."* when `false`.

## Where it is applied

### Agent API (`Features/AgentApi/Endpoints/SecretVaultEndpoints.cs`)

| Operation | Validation |
|-----------|------------|
| **POST** (create) | `TryResolveScope` with body scope → use effective scope; if forbidden, return 403. |
| **GET** (list) | `TryResolveScope` with query tenantId, agentId, activationName → list with effective scope. |
| **GET** /fetch | `TryResolveScope` with query scope → fetch with effective scope. |
| **GET** /{id} | Load secret; `CanAccessSecretTenant(tenantContext, secret.TenantId)` → if false, return 403. |
| **PUT** /{id} | Load secret; `CanAccessSecretTenant` → if false, return 403. Then `TryResolveScope` on update body → update with effective scope. |
| **DELETE** /{id} | Load secret; `CanAccessSecretTenant` → if false, return 403; then delete. |

### Admin API (`Features/AdminApi/Endpoints/AdminSecretVaultEndpoints.cs`)

Same rules as Agent API:

- Create, list, fetch: `TryResolveScope` and use effective scope; return 403 when forbidden.
- Get by id, update, delete: load secret, then `CanAccessSecretTenant`; if false, return 403. For update, also resolve scope from the body with `TryResolveScope`.

## Roles and context

- **SysAdmin**: `tenantContext.UserRoles` contains `SystemRoles.SysAdmin` (case-insensitive). No tenant restriction.
- **TenantAdmin / other**: No SysAdmin role. Tenant is taken from `tenantContext.TenantId` (certificate or logged-in user). All Secret Vault operations are restricted to that tenant; cross-tenant secrets (null tenant) are not accessible.

## HTTP responses

| Situation | Status | Body |
|-----------|--------|------|
| TenantAdmin sends request with different tenantId (create/list/fetch/update) | 403 Forbidden | `{ "error": "Access denied. Secret Vault operations are only allowed within your tenant." }` |
| Context has no tenant (non–SysAdmin) | 403 Forbidden | `{ "error": "Tenant scope is required. Certificate or user context has no tenant." }` |
| Get/Update/Delete on a secret in another tenant (or cross-tenant) | 403 Forbidden | `{ "error": "Access denied. Secret is not in your tenant." }` |

## Summary

| Aspect | Detail |
|--------|--------|
| **Purpose** | Restrict Secret Vault CRUD by tenant based on caller role and context. |
| **SysAdmin** | Any tenant; request scope used as-is; cross-tenant secrets accessible. |
| **TenantAdmin / non–SysAdmin** | Only `tenantContext.TenantId`; effective tenant forced; get/update/delete require secret in that tenant. |
| **APIs** | Agent API and Admin API both use `SecretVaultScopeEnforcement`. |
| **Create/List/Fetch/Update body** | `TryResolveScope` → effective scope or 403. |
| **Get by id / Update / Delete** | Load secret → `CanAccessSecretTenant` → 403 if not allowed. |

For general Secret Vault behaviour, encryption, and API shape, see [SECRET_VAULT.md](./SECRET_VAULT.md).
