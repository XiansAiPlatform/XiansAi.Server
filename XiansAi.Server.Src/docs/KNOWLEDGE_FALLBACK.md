# Knowledge Resolution & Fallback Mechanism

This document explains the exact algorithm used when either endpoint resolves a knowledge item by name. Both the Agent API and Admin API share the same underlying logic, implemented in `KnowledgeService.GetLatestByNameForTenantAsync`.

> **Single source of truth.** The fallback is implemented once in `GetLatestByNameForTenantAsync`; both endpoints call it directly. The short OpenAPI/Swagger description shown on both endpoints is also defined once, in `KnowledgeFallbackDocs.FallbackSummary` (`Shared/Services/KnowledgeService.cs`), so the two endpoints can never drift apart. This document is the long-form reference for that summary.

---

## Endpoints That Use This Mechanism

| Endpoint | Route | Caller |
|---|---|---|
| Agent API | `GET /api/agent/knowledge/latest` | `KnowledgeService.GetLatestByNameForTenantAsync(name, agent, tenantContext.TenantId, activationName)` |
| Admin API | `GET /api/v1/admin/tenants/{tenantId}/knowledge/latest` | `KnowledgeService.GetLatestByNameForTenantAsync(name, agentName, tenantId, activationName)` |

Both pass the same four arguments into the same service method. The only difference is that the Agent API derives `tenantId` from the request's certificate-authenticated tenant context, while the Admin API reads it explicitly from the URL path.

---

## Knowledge Document Fields

Every knowledge document in the `knowledge` MongoDB collection has these scope-determining fields:

| Field | MongoDB key | Type | Meaning |
|---|---|---|---|
| `Name` | `name` | `string` | Logical identifier for the knowledge item |
| `Agent` | `agent` | `string?` | The agent this knowledge belongs to |
| `TenantId` | `tenant_id` | `string?` | `null` for system-scoped; tenant ID string for tenant/activation-scoped |
| `SystemScoped` | `system_scoped` | `bool` | `true` only for system-scoped knowledge |
| `ActivationName` | `activation_name` | `string?` | `null` for tenant-default; non-null for activation-specific |
| `CreatedAt` | `created_at` | `DateTime` | Timestamp used to pick the **latest** version |

Multiple documents can share the same `Name + Agent` — every write creates a new document. The most recent document by `CreatedAt` wins within each step of the fallback.

---

## Scope Levels

```
System-scoped       → tenant_id = null,       system_scoped = true,  activation_name = null
Tenant-default      → tenant_id = <tenantId>, system_scoped = false, activation_name = null
Activation-specific → tenant_id = <tenantId>, system_scoped = false, activation_name = <name>
```

---

## The 3-Step Fallback Algorithm

```
GetLatestByNameForTenantAsync(name, agent, tenantId, activationName)
```

### Step 1 — Activation-specific match  
**Condition:** `activationName` is provided AND `tenantId` is not empty.

**DB query** (`GetLatestByNameAndActivationAsync`):
```
name          = <name>
agent         = <agent>
tenant_id     = <tenantId>
activation_name = <activationName>
ORDER BY created_at DESC
LIMIT 1
```

If a document is found → **return it. Stop.**

---

### Step 2 — Tenant-default match (strict activation isolation)  
**Condition:** `tenantId` is not empty.  
Runs regardless of whether `activationName` was supplied.

**DB query** (`GetLatestByNameAndTenantAsync`):
```
name            = <name>
agent           = <agent>
tenant_id       = <tenantId>
activation_name = null          ← only tenant-default documents
ORDER BY created_at DESC
LIMIT 1
```

This step matches **only** the tenant-default (`activation_name = null`). It will **never** return a document that belongs to a different activation. See [Activation Isolation](#activation-isolation) below.

If a document is found → **return it. Stop.**

---

### Step 3 — System-scoped fallback  
**Condition:** Always runs if Steps 1 and 2 both missed.

**DB query** (`GetLatestSystemByNameAsync`):
```
name          = <name>
agent         = <agent> OR agent = null   (agent-specific preferred over null-agent)
tenant_id     = null
system_scoped = true
ORDER BY agent = <agent> DESC, created_at DESC
LIMIT 1
```

The in-memory sort means an agent-specific system document beats a `null`-agent system document of the same name. Among documents with equal agent specificity, the most recent wins.

If a document is found → **return it.**  
If not found → **return 404 Not Found.**

---

## Decision Flow

```
Request: name, agent, tenantId, activationName
│
├─ activationName provided AND tenantId provided?
│   └─ YES → Query: name + agent + tenantId + activationName
│              Found? ──► RETURN (activation-specific)
│
├─ tenantId provided?
│   └─ YES → Query: name + agent + tenantId + activation_name=null (tenant-default only)
│              Found? ──► RETURN (tenant-default; never another activation)
│
└─ Query: name + (agent OR null-agent) + tenant_id=null + system_scoped=true
           Found? ──► RETURN (system-scoped, agent-specific preferred)
           Not found? ──► 404
```

---

## Input Combinations and Expected Behaviour

### Case 1 — Only `name` and `agent` provided (no `tenantId`, no `activationName`)

This happens when the request context has no tenant (e.g. a certificate with no tenant claim resolves `tenantId = null`).

- Step 1 is skipped (no `activationName` and no `tenantId`).
- Step 2 is skipped (no `tenantId`).
- Step 3 runs: returns system-scoped knowledge for that name+agent or 404.

---

### Case 2 — `name`, `agent`, `tenantId` provided; no `activationName`

- Step 1 is skipped (no `activationName`).
- Step 2 runs: finds the most recently created **tenant-default** document (`tenant_id = tenantId`, `agent = agent`, `activation_name = null`). Activation-specific documents are ignored.
- Step 3 runs only if Step 2 found nothing: returns system-scoped knowledge or 404.

---

### Case 3 — All four inputs provided (`name`, `agent`, `tenantId`, `activationName`)

- Step 1 runs: finds `tenant_id = tenantId` + `activation_name = activationName`. If found, returned immediately.
- Step 2 runs (if Step 1 missed): finds the most recently created **tenant-default** document (`activation_name = null`). It will **not** return any other activation's knowledge.
- Step 3 runs (if Step 2 missed): returns system-scoped knowledge or 404.

---

### Case 4 — `tenantId` provided but the tenant has no knowledge for this name+agent

- Steps 1 and 2 return nothing.
- Step 3 finds system-scoped knowledge: `agent = <agent>` beats `agent = null` if both exist with the same name.

---

## Activation Isolation

Activations are **strongly isolated**: the fallback never crosses from one activation to another.

- An **activation-specific request** (`activationName` supplied) resolves in this order only:
  1. the matching activation (`activation_name = activationName`),
  2. the tenant-default (`activation_name = null`),
  3. system-scoped.
  It will **never** be served another activation's knowledge.

- A **request with no activation** resolves to the tenant-default, then system-scoped. It will **never** be served any activation-specific knowledge.

### Consequence

If a tenant has *only* activation-specific knowledge for a name (e.g. `activation_name = "prod"`) and there is no tenant-default:

| Request | Result |
|---|---|
| `activationName = "prod"` | returns the `prod` document (Step 1) |
| `activationName = "dev"` | skips `prod`, falls through to tenant-default (none) → system-scoped or 404 |
| no `activationName` | skips `prod`, falls through to tenant-default (none) → system-scoped or 404 |

To provide a shared baseline across activations, create a **tenant-default** document (`activation_name = null`) for that knowledge name. Activation-specific documents then override it only for their own activation.

---

## Version Selection (within each step)

Each step returns only one document: the one with the **highest `created_at`** timestamp among all documents that match the query filters. There is no explicit version pinning — "latest" always means most recently inserted.

For knowledge that was updated via `PATCH`, a new document is always inserted (the old one is never mutated), so the latest `created_at` correctly identifies the current version.

---

## Source References

| Layer | File |
|---|---|
| Service (fallback logic) | `Shared/Services/KnowledgeService.cs` → `GetLatestByNameForTenantAsync` |
| Shared endpoint description | `Shared/Services/KnowledgeService.cs` → `KnowledgeFallbackDocs.FallbackSummary` |
| Repository Step 1 | `Shared/Repositories/KnowledgeRepository.cs` → `GetLatestByNameAndActivationAsync` |
| Repository Step 2 | `Shared/Repositories/KnowledgeRepository.cs` → `GetLatestByNameAndTenantAsync` |
| Repository Step 3 | `Shared/Repositories/KnowledgeRepository.cs` → `GetLatestSystemByNameAsync` |
| Agent API endpoint | `Features/AgentApi/Endpoints/KnowledgeEndpoints.cs` |
| Admin API endpoint | `Features/AdminApi/Endpoints/AdminKnowledgeEndpoints.cs` |
| Data model | `Shared/Data/Models/Knowledge.cs` |
