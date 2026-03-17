## Secret Vault

## Overview

The Secret Vault is a simple, secure store for key-value secrets with optional scoping by tenant, agent, user, and activation. The secret **value** is encrypted at rest using AES-256-GCM (via `ISecureEncryptionService`). The vault supports CRUD and a dedicated **fetch-by-key** operation with strict scope matching. Optional **additionalData** allows flat key-value metadata (string, number, or boolean values only), validated and sanitized on create/update.

## Architecture

### Components

| Component | Location | Role |
|-----------|----------|------|
| **SecretVault** (model) | `Shared/Data/Models/SecretVault.cs` | MongoDB document: key, encrypted value, scope (tenantId, agentId, userId, activationName), additionalData, audit fields |
| **ISecretVaultRepository** | `Shared/Repositories/SecretVaultRepository.cs` | Persistence: CRUD, find by key+scope, list with optional filters |
| **ISecretVaultService** | `Shared/Services/SecretVaultService.cs` | Business logic: encrypt/decrypt, validation/sanitization of additionalData, scope rules |
| **Admin API endpoints** | `Features/AdminApi/Endpoints/AdminSecretVaultEndpoints.cs` | REST under `/api/v1/admin/secrets` (API key auth) |
| **Agent API endpoints** | `Features/AgentApi/Endpoints/SecretVaultEndpoints.cs` | REST under `/api/agent/secrets` (client certificate auth) |

### Data Flow

```
Create/Update:
  Request (key, value, scope, additionalData)
  → Endpoint normalizes additionalData (object → JSON string)
  → Service validates & sanitizes additionalData (flat keys/string values only)
  → Service encrypts value with SecretVaultKey
  → Repository persists to MongoDB (collection: secret_vault)

Fetch by key:
  Request (key, tenantId?, agentId?, userId?, activationName?)
  → Repository FindForAccessAsync (strict scope match for all scopes)
  → Service decrypts value
  → Response { value, additionalData } (additionalData as JSON object)

List:
  → Repository load
  → Service parses additionalData to object for response
  → Response with metadata and additionalData as JSON object (no value)
```

### Scope Semantics

- **tenantId** (null = secret is available across all tenants)
- **agentId** (null = available across all agents)
- **userId** (null = any user may access; when set, only that user may access)
- **activationName** (null = any activation of the agent may access; when set, only that agent activation by name may access). An *activation* is a named instance of an agent (e.g. workflow ID postfix).

**Fetch-by-key** uses **strict** scope matching for all scopes (tenantId, agentId, userId, activationName):

- If the **request** omits a scope (e.g. no `tenantId`), only secrets with that scope **null** in the document are returned (e.g. cross-tenant secrets).
- If the **request** sends a scope (e.g. `tenantId=99xio`), the **document** must have that exact value. A document with `tenant_id: null` will not match a request that sends `tenantId=99xio`.

So: to fetch a secret scoped to a tenant, the request must include that tenantId (and similarly for agentId, userId, activationName when the secret is scoped).

### Tenant Existence Validation

- **Agent API**:
  - For **create**, **update**, and **fetch** operations, when a tenant scope is resolved (non-empty `tenantId`), the endpoint first verifies that the tenant exists using `ITenantCacheService.GetByTenantIdAsync`.
  - If the tenant does not exist, the request fails with **404** `"Tenant not found"` and the secret operation is not performed.

- **Admin API**:
  - When creating or updating a secret **and** a `tenantId` scope is explicitly provided in the request, the resolved tenant ID is validated via `ITenantCacheService.GetByTenantIdAsync`.
  - If the tenant does not exist, the request fails with **404** `"Tenant not found"` and the secret is not created/updated.

This ensures that all tenant-scoped secrets always reference a real tenant in the Xians tenant collection, for both Agent and Admin flows.

### Key and Scope Uniqueness

Secret uniqueness is enforced on the **combination** of key and full scope tuple:

- `Key`
- `TenantId`
- `AgentId`
- `UserId`
- `ActivationName`

This means:

- You **can** create multiple secrets with the same key as long as their **scope combination is different** (e.g. different tenant, agent, user, or activation).
- You **cannot** create or update a secret such that there are two documents with the **same key and identical scope tuple** (`tenantId`, `agentId`, `userId`, `activationName`).

Implementation details:

- `SecretVaultRepository.ExistsByKeyAndScopeAsync` checks whether a document already exists for a given key and scope (with an optional `excludeId` for updates).
- `SecretVaultService.CreateAsync` normalizes scope values (whitespace → `null`) and uses `ExistsByKeyAndScopeAsync` to prevent duplicates at create time.
- `SecretVaultService.UpdateAsync` re-checks `ExistsByKeyAndScopeAsync` after applying scope changes, excluding the current document via `excludeId`, to avoid collisions when changing scope.

This guarantees that **within a single scope combination, the key is unique**, while still allowing reuse of the same key across different scopes.

## Configuration

### Encryption Key

The vault uses a **dedicated** unique secret for encryption (same pattern as Tenant OIDC config): `EncryptionKeys:UniqueSecrets:SecretVaultKey`. If not set, the service falls back to `EncryptionKeys:BaseSecret`.

Add to `appsettings.json` (or environment-specific config):

```json
{
  "EncryptionKeys": {
    "BaseSecret": "your-base-secret-at-least-32-chars",
    "UniqueSecrets": {
      "TenantOidcSecretKey": "...",
      "SecretVaultKey": "xiansai_secret_vault_encryption_key_32bytes!!"
    }
  }
}
```

- Use a **unique** value for `SecretVaultKey` (at least 32 characters recommended).
- Do not commit production keys; use environment variables or a secret manager.

### Service Registration

The repository and service are registered in **SharedConfiguration**:

- `ISecretVaultRepository` → `SecretVaultRepository` (scoped)
- `ISecretVaultService` → `SecretVaultService` (scoped)

Admin and Agent APIs use the same shared service.

## API Reference

### Admin API (`/api/v1/admin/secrets`)

Authentication: **API key** (Bearer token). Policy: `AdminEndpointAuthPolicy` (e.g. SysAdmin).

| Method | Path | Description |
|--------|------|-------------|
| POST | `/` | Create secret. Key must be unique. Body: key, value, tenantId?, agentId?, userId?, activationName?, additionalData? |
| GET | `/` | List secrets. Query: tenantId?, agentId?, activationName? (optional filters) |
| GET | `/fetch?key=&tenantId=&agentId=&userId=&activationName=` | Fetch secret by key with strict scope; returns `{ value, additionalData }` only |
| PUT | `/` | Update secret (value, scope, additionalData) by key+scope |
| DELETE | `/` | Delete secret by key+scope (query: key, tenantId?, agentId?, userId?, activationName?) |

**CreatedBy / UpdatedBy** are set from `ITenantContext.LoggedInUser` (fallback `"system"`).

### Agent API (`/api/agent/secrets`)

Authentication: **Client certificate** (Bearer token = Base64-encoded client certificate). Policy: `RequiresCertificate`.

Same operations as Admin API (create by body, list, fetch by key, update by key+scope, delete by key+scope) under base path `/api/agent/secrets`. Actor for CreatedBy/UpdatedBy is `"agent-api"` (no user context in certificate flow).

### Request / Response Shapes

**Create (POST) body**

```json
{
  "key": "my-api-key",
  "value": "the-secret-value",
  "tenantId": null,
  "agentId": null,
  "userId": null,
  "activationName": null,
  "additionalData": { "env": "prod", "service": "payment", "count": 42, "enabled": true }
}
```

- **additionalData** is optional. It can be a **JSON object** (recommended) or a JSON string. It must be a flat object with values that are **string**, **number**, or **boolean** only (see Validation & Sanitization below).

**Fetch response** (GET `/fetch?key=...`)

```json
{
  "value": "the-secret-value",
  "additionalData": { "env": "prod", "service": "payment" }
}
```

**Create response** (full secret record)

- Same as above, plus: `id`, `key`, `tenantId`, `agentId`, `userId`, `activationName`, `createdAt`, `createdBy`, `updatedAt`, `updatedBy`. The `value` is decrypted; `additionalData` is returned as a JSON object. Subsequent fetch/update/delete operations address the secret by **key + scope**, not by id.

## AdditionalData: Structure and Security

### Allowed Shape

- **Only** a flat JSON object: keys and values. **Values** may be **string**, **number**, or **boolean** only.
- Nested objects or arrays are **not** allowed and will be rejected with a 400 error.

### Validation and Sanitization (Create & Update)

Applied on every create and update:

1. **Keys**
   - Allowed characters: `a-zA-Z0-9_.-` only. Any other character is removed.
   - Max length: 128 characters (truncated if longer).
   - Empty keys after sanitization are skipped.

2. **Values** (only string, number, or boolean)
   - **String:** Control characters (0x00–0x1F) are stripped, except tab, newline, carriage return. Max length 2048 characters (truncated if longer).
   - **Number:** Stored as integer (Int64) or double. Unrepresentable numbers are skipped.
   - **Boolean:** Stored as `true` or `false`.
   - Nested objects or arrays: request is rejected with *"additionalData values must be string, number, or boolean only; nested objects or arrays are not allowed."*

3. **Limits**
   - Max 50 keys per additionalData object.
   - Max total size of serialized additionalData: 8 KB. Exceeding returns 400.

4. **Invalid JSON**
   - If the provided additionalData is not valid JSON, the API returns 400: *"additionalData must be valid JSON."*

Stored value is the **sanitized** JSON string (types preserved for number and boolean). On read, it is parsed and returned as a JSON object in the response.

## Database

- **Collection:** `secret_vault`
- **Document fields:** `_id`, `key`, `encrypted_value`, `tenant_id`, `agent_id`, `user_id`, `activation_name`, `additional_data`, `created_at`, `created_by`, `updated_at`, `updated_by`
- **Uniqueness:** The combination of **key + scope tuple** (`tenantId`, `agentId`, `userId`, `activationName`) is unique. The same key can exist in multiple rows as long as their scope differs.

## Security Considerations

- **Encryption:** The secret value is encrypted with `ISecureEncryptionService` using the vault-specific key. Do not reuse the same key for other features.
- **Scope:** Use tenantId/agentId/userId/activationName to limit who can fetch a secret (strict match on fetch for all scopes).
- **AdditionalData:** Not for sensitive secrets; it is stored in plain JSON (sanitized). Use it for metadata (e.g. env, service name) only.
- **Key management:** Store `SecretVaultKey` in a secret manager in production; rotate according to policy (rotation will require re-encrypting values if you need to support old keys).

## HTTP Test Files

- **Admin API:** `XiansAi.Server.Tests/http/AdminApi/admin-secret-vault-endpoints.http` (Bearer API key).
- **Agent API:** `XiansAi.Server.Tests/http/AgentApi/agent-secret-vault-endpoints.http` (Bearer = Base64 client certificate).

## Summary

| Aspect | Detail |
|--------|--------|
| **Storage** | MongoDB collection `secret_vault`; value encrypted, additionalData as sanitized JSON string |
| **APIs** | Admin API (API key) and Agent API (client cert); same service, different auth |
| **Scope** | tenantId, agentId, userId, activationName; null = “any”; fetch uses strict scope match for all; uniqueness is enforced on key + full scope tuple |
| **AdditionalData** | Flat key-value; values: string, number, or boolean only; validated and sanitized on create/update; returned as JSON object |
| **Encryption** | `EncryptionKeys:UniqueSecrets:SecretVaultKey` (fallback: BaseSecret) via `ISecureEncryptionService` |
