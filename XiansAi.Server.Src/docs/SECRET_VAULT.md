# Secret Vault

## Overview

The Secret Vault is a simple, secure store for key-value secrets with optional scoping by tenant, agent, and user. The secret **value** is persisted via a **pluggable [Secret Store provider](../Shared/Providers/SecretStore/README.md)** — by default an AES-256-GCM ciphertext in MongoDB, optionally Azure Key Vault. The vault supports CRUD and a dedicated **fetch-by-key** operation with strict scope matching. Optional **additionalData** allows flat key-value metadata (string keys and string values only), validated and sanitized on create/update.

## Architecture

### Components

| Component | Location | Role |
|-----------|----------|------|
| **SecretVault** (model) | `Shared/Data/Models/SecretVault.cs` | MongoDB document: key, scope (tenantId, agentId, userId, activationName), additionalData, audit fields. Value is **not** stored on this document. |
| **ISecretVaultRepository** | `Shared/Repositories/SecretVaultRepository.cs` | Metadata persistence: CRUD, find by key+scope, list with optional filters |
| **ISecretStoreProvider** | `Shared/Providers/SecretStore/` | Pluggable backend for the secret value: `DatabaseSecretStoreProvider` (default) or `AzureKeyVaultSecretStoreProvider` |
| **ISecretVaultService** | `Shared/Services/SecretVaultService.cs` | Business logic: validation/sanitization of additionalData, scope rules, orchestrates metadata + provider |
| **Admin API endpoints** | `Features/AdminApi/Endpoints/AdminSecretVaultEndpoints.cs` | REST under `/api/v1/admin/secrets` (API key auth) |
| **Agent API endpoints** | `Features/AgentApi/Endpoints/SecretVaultEndpoints.cs` | REST under `/api/agent/secrets` (client certificate auth) |

### Data Flow

```
Create:
  Request (key, value, scope, additionalData)
  → Endpoint normalizes additionalData (object → JSON string)
  → Service validates & sanitizes additionalData (flat keys/string values only)
  → Service generates secretId, calls ISecretStoreProvider.SetAsync(secretId, value)
  → Repository persists metadata to MongoDB (collection: secret_vault)
  → On metadata-insert failure, value is rolled back via ISecretStoreProvider.DeleteAsync

Update / Delete:
  → Same orchestration: provider operation runs alongside the metadata write,
    with best-effort cleanup on partial failure.

Fetch by key:
  Request (key, tenantId?, agentId?, userId?, activationName?)
  → Repository FindForAccessAsync (strict scope match for all scopes)
  → Service calls ISecretStoreProvider.GetAsync(metadata.Id)
  → Response { value, additionalData } (additionalData as JSON object)

Get by id / List:
  → Repository load
  → Service calls provider for value (get), parses additionalData to object for response
  → Response with value and additionalData as JSON object
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

## Configuration

### Secret Store Provider

Pick one backend at startup via `SecretStore:Provider`. See [`Shared/Providers/SecretStore/README.md`](../Shared/Providers/SecretStore/README.md) for full provider docs.

```json
{
  "SecretStore": {
    "Provider": "database",
    "AzureKeyVault": {
      "VaultUri": "https://my-vault.vault.azure.net/",
      "SecretNamePrefix": "xians-"
    }
  }
}
```

| Value | Behavior |
|-------|----------|
| `database` (default) | Encrypts each value with AES-256-GCM and stores it in MongoDB collection `secret_vault_values`. |
| `azurekeyvault` | Stores each value as a Key Vault secret named `{prefix}{secretId}`, authenticated via `DefaultAzureCredential`. Requires `SecretStore:AzureKeyVault:VaultUri`. |

### Encryption Keys (database provider only)

The `database` provider uses the application-wide `ISecureEncryptionService` (`EncryptionKeys:BaseSecret`). The per-row salt input is the secret id itself, so each row uses a distinct AES key.

`EncryptionKeys:UniqueSecrets:SecretVaultKey` is **legacy** — it is only consulted to decrypt rows that were written before the provider abstraction. New writes never use it. After all old rows have been migrated (or expired), this key can be removed from configuration.

```json
{
  "EncryptionKeys": {
    "BaseSecret": "your-base-secret-at-least-32-chars",
    "UniqueSecrets": {
      "SecretVaultKey": "<legacy only — for upgrade fallback>"
    }
  }
}
```

- Do not commit production keys; use environment variables or a secret manager.

### Service Registration

Registered in **SharedConfiguration** / **SharedServices**:

- `ISecretVaultRepository` → `SecretVaultRepository` (scoped)
- `ISecretVaultService` → `SecretVaultService` (scoped)
- `ISecretStoreProvider` → `DatabaseSecretStoreProvider` or `AzureKeyVaultSecretStoreProvider` (scoped) — chosen by `SecretStoreProviderFactory.RegisterProvider` based on `SecretStore:Provider`.

Admin and Agent APIs use the same shared service.

## API Reference

### Admin API (`/api/v1/admin/secrets`)

Authentication: **API key** (Bearer token). Policy: `AdminEndpointAuthPolicy` (e.g. SysAdmin).

The Admin API can **manage** secrets (create / update / delete / list / probe by key) but **never** returns the secret value. All read endpoints return metadata only (id, key, scope, additionalData, audit). To read a value, use the Agent API with a valid client certificate.

| Method | Path | Description |
|--------|------|-------------|
| POST | `/` | Create secret. Key must be unique. Body: key, value, tenantId?, agentId?, userId?, activationName?, additionalData?. Response is **metadata only** (no value echo). |
| GET | `/` | List secrets metadata. Query: tenantId?, agentId?, activationName? (optional filters). |
| GET | `/fetch?key=&tenantId=&agentId=&userId=&activationName=` | Probe by key with strict scope. Returns metadata only — no value. Useful to confirm a scoped secret exists. |
| GET | `/{id}` | Get secret metadata by id. **Value is never returned to admin callers.** |
| PUT | `/{id}` | Update secret (value, scope, additionalData). Response is metadata only. |
| DELETE | `/{id}` | Delete secret. |

**CreatedBy / UpdatedBy** are set from `ITenantContext.LoggedInUser` (fallback `"system"`).

### Agent API (`/api/agent/secrets`)

Authentication: **Client certificate** (Bearer token = Base64-encoded client certificate). Policy: `RequiresCertificate`.

Same management operations as Admin API, under base path `/api/agent/secrets`. **Unlike the Admin API**, Agent endpoints return the decrypted value on `GET /{id}`, `GET /fetch`, and on Create/Update responses, because agents legitimately need to consume the secret value at runtime. Actor for CreatedBy/UpdatedBy is `"agent-api"` (no user context in certificate flow).

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

**Agent fetch response** (GET `/api/agent/secrets/fetch?key=...`)

```json
{
  "value": "the-secret-value",
  "additionalData": { "env": "prod", "service": "payment" }
}
```

**Agent get by id / create / update response** (full record, with value)

- Includes: `id`, `key`, `value`, `tenantId`, `agentId`, `userId`, `activationName`, `additionalData`, `createdAt`, `createdBy`, `updatedAt`, `updatedBy`. The `value` is decrypted; `additionalData` is returned as a JSON object.

**Admin fetch / get / create / update response** (metadata only — value is never returned)

```json
{
  "id": "...",
  "key": "my-api-key",
  "tenantId": null,
  "agentId": null,
  "userId": null,
  "activationName": null,
  "additionalData": { "env": "prod" },
  "createdAt": "...",
  "createdBy": "...",
  "updatedAt": "...",
  "updatedBy": "..."
}
```

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

- **Metadata collection:** `secret_vault`
- **Document fields:** `_id`, `key`, `tenant_id`, `agent_id`, `user_id`, `activation_name`, `additional_data`, `created_at`, `created_by`, `updated_at`, `updated_by`. The legacy `encrypted_value` field is read on fallback only and is no longer written by new code.
- **Uniqueness:** The secret **key** is unique in the collection (one document per key).
- **Value collection (database provider only):** `secret_vault_values` with `_id` matching the metadata `_id` and a `ciphertext` field. Lookup is by `_id`; no extra indexes required.

## Security Considerations

- **At-rest encryption:** The database provider encrypts values with `ISecureEncryptionService` (AES-256-GCM, PBKDF2 200k iterations). The per-row salt is the secret id, so each row uses a distinct AES key derived from `BaseSecret`. The Azure Key Vault provider relies on Azure-managed encryption.
- **Transport:** TLS to MongoDB / Azure Key Vault.
- **Scope:** Use tenantId/agentId/userId/activationName to limit who can fetch a secret (strict match on fetch for all scopes).
- **AdditionalData:** Not for sensitive secrets; it is stored in plain JSON (sanitized). Use it for metadata (e.g. env, service name) only.
- **No value logging:** The service never logs secret plaintext or ciphertext. Audit lines include id, key, tenant, actor, and provider name only.
- **Key management:** Store `BaseSecret` (and any Azure Key Vault credentials) in a secret manager in production. The legacy `SecretVaultKey` should be removed once no rows depend on it.
- **Switching providers:** Switching `SecretStore:Provider` is **not** an automatic data migration. Existing values written by one provider are not visible to the other; an explicit copy is required.

## HTTP Test Files

- **Admin API:** `XiansAi.Server.Tests/http/AdminApi/admin-secret-vault-endpoints.http` (Bearer API key).
- **Agent API:** `XiansAi.Server.Tests/http/AgentApi/agent-secret-vault-endpoints.http` (Bearer = Base64 client certificate).

## Summary

| Aspect | Detail |
|--------|--------|
| **Storage** | Metadata in MongoDB `secret_vault`; value via pluggable `ISecretStoreProvider` (database default → `secret_vault_values`, or Azure Key Vault) |
| **APIs** | Admin API (API key) and Agent API (client cert); same service, different auth |
| **Scope** | tenantId, agentId, userId, activationName; null = "any"; fetch uses strict scope match for all |
| **AdditionalData** | Flat key-value; values: string, number, or boolean only; validated and sanitized on create/update; returned as JSON object |
| **Provider selection** | `SecretStore:Provider` = `database` \| `azurekeyvault` (startup-time, global) |
| **Encryption (database provider)** | AES-256-GCM via `ISecureEncryptionService`; per-record key derived from `BaseSecret` + secret id |
