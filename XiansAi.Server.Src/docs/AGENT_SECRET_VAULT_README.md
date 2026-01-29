# Agent Secret Vault (Provider-Separated Design)

## Overview

The Agent Secret Vault provides secure storage and retrieval of secrets (JWT tokens, API keys, OAuth tokens, etc.) for agents.  
In **MongoDB**, **Azure Key Vault**, **AWS Secrets Manager**, and **HashiCorp Vault** are treated as **separate vault providers**.

This is **not a hybrid approach**. When a Key Vault provider is selected, **no secret data is stored in MongoDB at all** (no metadata record either). When the Database provider is selected, **MongoDB is the only store**.

## Core Rules

- **Provider is exclusive**: exactly one provider is active at runtime.
- **Database provider**:
  - Store the secret object in MongoDB.
  - Encrypt **`secretValue`**, **`metadata`**, and **`expireAt`** before persisting.
- **Key Vault providers (Azure/AWS/HashiCorp)**:
  - Store the **entire secret data object** as a **serialized JSON string** in the vault secret value.
  - Do **not** create or update any MongoDB record for the secret.
- **Encryption implementation**:
  - Do **not** introduce new encryption services.
  - Use `SecureEncryptionService` (`v2/XiansAi.Server/XiansAi.Server.Src/Shared/Services/SecureEncryptionService.cs`) exactly like `TenantOidcConfigService` does (encrypt/decrypt a payload string using `EncryptionKeys` from configuration).

## Scoping Model

Secrets are scoped by:

- **System scope**: `tenantId = null` (or `"system"` in API routes)  
- **Tenant scope**: `tenantId = <tenant>`
- **Agent scope**: `agentId = null` (global) or `agentId = <agent>`
- **User scope**: `userId/participantId = null` (global) or `userId/participantId = <user>`

In, **scope participates in the Key Vault secret name**, so lookup does not require any database index or cross-store reference.

## Data Model (Canonical Secret Object)

This is the canonical object shape used across providers.  
For Key Vault providers, **this whole object is serialized to JSON** and stored as the vault secret value.

```json
{
  "secretId": "openai-api-key",
  "tenantId": "tenant-123",
  "agentId": "agent-456",
  "userId": "user-789",
  "secretValue": "sk-...",
  "metadata": "{\"environment\":\"production\",\"region\":\"us-east-1\"}",
  "description": "OpenAI API key for agent",
  "expireAt": "2026-12-31T23:59:59Z",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com",
  "updatedAt": "2026-01-10T12:00:00Z",
  "updatedBy": "admin@example.com"
}
```

**Notes**
- `metadata` is a JSON string (user-defined) and is considered sensitive.
- `expireAt` is considered sensitive and must be encrypted when using the Database provider.

## Provider Behavior

### Provider: MongoDB (`database`)

**Storage**
- Persist a MongoDB document per secret.
- Encrypt at rest:
  - `secretValue` (required)
  - `metadata` (optional)
  - `expireAt` (optional)

**Encryption**
- Use `SecureEncryptionService.Encrypt(plaintext, uniqueSecret)` to encrypt each sensitive field.
- Use `SecureEncryptionService.Decrypt(ciphertext, uniqueSecret)` to decrypt.
- The `uniqueSecret` for the secret vault comes from:
  - `EncryptionKeys:UniqueSecrets:SecretVaultKey` (preferred)
  - fallback: `EncryptionKeys:BaseSecret` (same fallback behavior as `TenantOidcConfigService`)

**Example: Stored MongoDB document (conceptual)**

```json
{
  "id": "mongodb-object-id",
  "tenantId": "tenant-123",
  "agentId": "agent-456",
  "userId": "user-789",
  "secretId": "openai-api-key",
  "encryptedSecretValue": "<base64>",
  "encryptedMetadata": "<base64>",
  "encryptedExpireAt": "<base64>",
  "description": "OpenAI API key for agent",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com",
  "updatedAt": "2026-01-10T12:00:00Z",
  "updatedBy": "admin@example.com"
}
```

### Provider: Azure Key Vault (`azure`)

**Storage**
- Store **one Key Vault secret** per logical secret scope.
- The secret **value** is the **serialized JSON** of the canonical secret object.
- **No MongoDB record** is created.

### Provider: AWS Secrets Manager (`aws`)

Same behavior as Azure:
- Secret **value** is the serialized JSON canonical object.
- **No MongoDB record**.

### Provider: HashiCorp Vault (`hashicorp`)

Same behavior as Azure:
- Secret **value** is the serialized JSON canonical object.
- **No MongoDB record**.

## Key Vault Secret Naming Convention

Key Vault providers need a deterministic name so we can create/fetch the serialized JSON without a DB reference.

### Name Format

```
{secretId}--{tenantId}--{agentId}--{userId}
```

### Normalization Rules
- **Null values** are represented as:
  - `tenantId`: `system`
  - `agentId`: `global`
  - `userId`: `global`
- Replace unsupported characters with `-` and collapse repeats:
  - Allowed set recommendation: `[A-Za-z0-9-]`
  - Convert to lowercase

### Examples
- Tenant-scoped + agent-scoped + user-scoped:
  - `openai-api-key--tenant-123--agent-456--user-789`
- System + global agent + global user:
  - `openai-api-key--system--global--global`

### Why this format?
- Deterministic lookup by `(secretId, tenantId, agentId, userId)` without MongoDB.
- Supports full separation between stores (no hybrid indexing/reference).
- Easy to debug and inspect in vault consoles.

## Serialization Contract (Key Vault Providers)

- Serialize the canonical secret object to JSON (e.g., `System.Text.Json`).
- Store it as the vault secret value.
- On read:
  - Fetch vault secret value by the deterministic secret name.
  - Deserialize JSON into the canonical secret object.

**Important**: when a Key Vault provider is selected, the vault value is the source of truth for:
- `secretValue`
- `metadata`
- `expireAt`
- all other fields in the object

## Configuration

### Encryption Keys (`appsettings.json`)

The Secret Vault uses the same encryption-key pattern as `TenantOidcConfigService`.

```json
{
  "EncryptionKeys": {
    "BaseSecret": "…",
    "UniqueSecrets": {
      "TenantOidcSecretKey": "…",
      "SecretVaultKey": "your-unique-secret-vault-key-optional"
    }
  }
}
```

- Prefer `EncryptionKeys:UniqueSecrets:SecretVaultKey` for vault encryption.
- If missing, fall back to `EncryptionKeys:BaseSecret`.

### Provider Selection

`SecretVault:Provider` selects the provider:
- `database`
- `azure`
- `aws`
- `hashicorp`

Example:

```json
{
  "SecretVault": {
    "Provider": "database",
    "Azure": {
      "VaultUrl": "https://your-vault-name.vault.azure.net/",
      "TenantId": "optional-tenant-id",
      "ClientId": "optional-client-id",
      "ClientSecret": "optional-client-secret"
    }
  }
}
```

## Operational Flows

### Create Secret
- **Database provider**:
  - Encrypt `secretValue`, `metadata`, `expireAt`
  - Insert MongoDB document
- **Key Vault provider**:
  - Build canonical secret object
  - Compute secret name: `{secretId}--{tenantId}--{agentId}--{userId}`
  - Serialize to JSON and store as vault secret value
  - Do not touch MongoDB

### Get Secret
- **Database provider**:
  - Query MongoDB by `secretId + scopes`
  - Decrypt `secretValue`, `metadata`, `expireAt`
- **Key Vault provider**:
  - Compute secret name from request scopes
  - Fetch vault secret
  - Deserialize JSON and return

### Update Secret
- **Database provider**:
  - Update encrypted fields in MongoDB
- **Key Vault provider**:
  - Read existing JSON (optional)
  - Apply partial updates to object
  - Re-serialize and overwrite vault secret value
  - Do not touch MongoDB

### Delete Secret
- **Database provider**:
  - Delete MongoDB document
- **Key Vault provider**:
  - Delete vault secret by deterministic name
  - Do not touch MongoDB

## API Endpoints

### AdminApi Endpoints

**Base Path**: `/api/v1/admin/tenants/{tenantId}/secrets`

**Authentication**: Requires `AdminEndpointAuthPolicy` (API key via `X-Admin-Api-Key` header)

#### GET `/api/v1/admin/tenants/{tenantId}/secrets`

Lists all secrets with optional filtering.

**Query Parameters:**
- `agentId` (string, optional): Filter by agent scope
- `userId` (string, optional): Filter by user scope
- `secretId` (string, optional): Filter by secret ID pattern (supports regex)
- `page` (int, optional): Page number (default: 1)
- `pageSize` (int, optional): Items per page (default: 50, max: 100)

**Response: 200 OK**
```json
{
  "items": [
    {
      "secretId": "openai-api-key",
      "tenantId": "tenant-123",
      "agentId": "agent-456",
      "userId": "user-789",
      "description": "OpenAI API key",
      "createdAt": "2026-01-01T10:30:00Z",
      "createdBy": "admin@example.com",
      "updatedAt": "2026-01-10T12:00:00Z",
      "updatedBy": "admin@example.com"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 50
}
```

**Note:** `secretValue`, `metadata`, and `expireAt` are never returned in list responses for security.

---

#### GET `/api/v1/admin/tenants/{tenantId}/secrets/{secretId}`

Retrieves a specific secret by ID.

**Path Parameters:**
- `tenantId` (string, required): Tenant identifier
- `secretId` (string, required): Secret identifier

**Query Parameters:**
- `agentId` (string, optional): Agent scope filter
- `userId` (string, optional): User scope filter
- `includeValue` (bool, optional): Include decrypted secret value, metadata, and expireAt (default: false)

**Response: 200 OK (without value)**
```json
{
  "secretId": "openai-api-key",
  "tenantId": "tenant-123",
  "agentId": "agent-456",
  "userId": "user-789",
  "description": "OpenAI API key",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com",
  "updatedAt": "2026-01-10T12:00:00Z",
  "updatedBy": "admin@example.com"
}
```

**Response: 200 OK (with value, includeValue=true)**
```json
{
  "secretId": "openai-api-key",
  "tenantId": "tenant-123",
  "agentId": "agent-456",
  "userId": "user-789",
  "secretValue": "sk-...",
  "metadata": "{\"environment\":\"production\",\"region\":\"us-east-1\"}",
  "description": "OpenAI API key",
  "expireAt": "2026-12-31T23:59:59Z",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com",
  "updatedAt": "2026-01-10T12:00:00Z",
  "updatedBy": "admin@example.com"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

---

#### POST `/api/v1/admin/tenants/{tenantId}/secrets`

Creates a new secret.

**Path Parameters:**
- `tenantId` (string, required): Tenant identifier (use `system` for system scope)

**Request Body:**
```json
{
  "secretId": "openai-api-key",
  "agentId": "agent-456",
  "userId": "user-789",
  "secretValue": "sk-...",
  "description": "OpenAI API key",
  "metadata": "{\"environment\":\"production\"}",
  "expireAt": "2026-12-31T23:59:59Z"
}
```

**Response: 201 Created**
```json
{
  "secretId": "openai-api-key",
  "tenantId": "tenant-123",
  "agentId": "agent-456",
  "userId": "user-789",
  "description": "OpenAI API key",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com"
}
```

**Response: 400 Bad Request**
```json
{
  "error": "BadRequest",
  "message": "Secret with ID 'openai-api-key' already exists in this scope"
}
```

---

#### PATCH `/api/v1/admin/tenants/{tenantId}/secrets/{secretId}`

Updates an existing secret. Only provided fields are updated.

**Path Parameters:**
- `tenantId` (string, required): Tenant identifier
- `secretId` (string, required): Secret identifier

**Query Parameters:**
- `agentId` (string, optional): Agent scope filter
- `userId` (string, optional): User scope filter

**Request Body:**
```json
{
  "secretValue": "sk-new-value",
  "description": "Updated description",
  "agentId": "new-agent-scope",
  "userId": "new-user-scope",
  "metadata": "{\"environment\":\"staging\"}",
  "expireAt": "2026-12-31T23:59:59Z"
}
```

**Response: 200 OK**
```json
{
  "secretId": "openai-api-key",
  "tenantId": "tenant-123",
  "agentId": "new-agent-scope",
  "userId": "new-user-scope",
  "description": "Updated description",
  "createdAt": "2026-01-01T10:30:00Z",
  "createdBy": "admin@example.com",
  "updatedAt": "2026-01-10T12:00:00Z",
  "updatedBy": "admin@example.com"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

**Response: 403 Forbidden**
```json
{
  "error": "Forbidden",
  "message": "Access denied: insufficient permissions to modify this secret"
}
```

---

#### DELETE `/api/v1/admin/tenants/{tenantId}/secrets/{secretId}`

Deletes a secret by ID.

**Path Parameters:**
- `tenantId` (string, required): Tenant identifier
- `secretId` (string, required): Secret identifier

**Query Parameters:**
- `agentId` (string, optional): Agent scope filter
- `userId` (string, optional): User scope filter

**Response: 200 OK**
```json
{
  "message": "Secret 'openai-api-key' deleted successfully"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

---

### AgentApi Endpoints

**Base Path**: `/api/agent/secrets`

**Authentication**: Requires client certificate (`.RequiresCertificate()`)

#### GET `/api/agent/secrets/{secretId}`

Retrieves a secret by its `secretId`. The system automatically resolves scopes based on the agent's context.

**Path Parameters:**
- `secretId` (string, required): The key identifier for the secret

**Query Parameters:**
- `agentId` (string, optional): Override agent scope if needed
- `userId` (string, optional): Override user scope if needed

**Response: 200 OK**
```json
{
  "secretId": "openai-api-key",
  "secretValue": "sk-...",
  "metadata": "{\"environment\":\"production\",\"region\":\"us-east-1\"}",
  "description": "OpenAI API key for agent",
  "expireAt": "2026-12-31T23:59:59Z",
  "createdAt": "2026-01-01T10:30:00Z",
  "updatedAt": "2026-01-10T12:00:00Z"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

**Response: 403 Forbidden**
```json
{
  "error": "Forbidden",
  "message": "Access denied: insufficient permissions to access this secret"
}
```

---

#### GET `/api/agent/secrets`

Lists all secrets accessible to the agent. The system automatically resolves scopes based on the agent's context.

**Query Parameters:**
- `agentId` (string, optional): Override agent scope if needed
- `userId` (string, optional): Override user scope if needed
- `page` (int, optional): Page number (default: 1)
- `pageSize` (int, optional): Items per page (default: 50, max: 100)

**Response: 200 OK**
```json
{
  "items": [
    {
      "secretId": "openai-api-key",
      "description": "OpenAI API key for agent",
      "createdAt": "2026-01-01T10:30:00Z",
      "updatedAt": "2026-01-10T12:00:00Z"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 50
}
```

**Note:** `secretValue`, `metadata`, and `expireAt` are never returned in list responses for security.

---

#### POST `/api/agent/secrets`

Creates a new secret. The system automatically resolves scopes based on the agent's context.

**Request Body:**
```json
{
  "secretId": "openai-api-key",
  "secretValue": "sk-...",
  "description": "OpenAI API key",
  "metadata": "{\"environment\":\"production\"}",
  "expireAt": "2026-12-31T23:59:59Z"
}
```

**Response: 201 Created**
```json
{
  "secretId": "openai-api-key",
  "description": "OpenAI API key",
  "createdAt": "2026-01-01T10:30:00Z"
}
```

**Response: 400 Bad Request**
```json
{
  "error": "BadRequest",
  "message": "Secret with ID 'openai-api-key' already exists in this scope"
}
```

---

#### PATCH `/api/agent/secrets/{secretId}`

Updates an existing secret. Only provided fields are updated. The system automatically resolves scopes based on the agent's context.

**Path Parameters:**
- `secretId` (string, required): The key identifier for the secret

**Request Body:**
```json
{
  "secretValue": "sk-new-value",
  "description": "Updated description",
  "metadata": "{\"environment\":\"staging\"}",
  "expireAt": "2026-12-31T23:59:59Z"
}
```

**Response: 200 OK**
```json
{
  "secretId": "openai-api-key",
  "description": "Updated description",
  "createdAt": "2026-01-01T10:30:00Z",
  "updatedAt": "2026-01-10T12:00:00Z"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

**Response: 403 Forbidden**
```json
{
  "error": "Forbidden",
  "message": "Access denied: insufficient permissions to modify this secret"
}
```

---

#### DELETE `/api/agent/secrets/{secretId}`

Deletes a secret by ID. The system automatically resolves scopes based on the agent's context.

**Path Parameters:**
- `secretId` (string, required): The key identifier for the secret

**Response: 200 OK**
```json
{
  "message": "Secret 'openai-api-key' deleted successfully"
}
```

**Response: 404 Not Found**
```json
{
  "error": "NotFound",
  "message": "Secret with ID 'openai-api-key' not found"
}
```

**Response: 403 Forbidden**
```json
{
  "error": "Forbidden",
  "message": "Access denied: insufficient permissions to delete this secret"
}
```

---

## Security Considerations

- **No mixed-source secrets**: don't fetch from DB when provider is a vault, and don't fetch from vault when provider is DB.
- **Least privilege**:
  - DB provider: restrict MongoDB collection access.
  - Vault providers: restrict secret read/write permissions to only the required prefixes/namespaces.
- **Avoid leaking scopes**: if vault naming requires obscuring IDs, introduce a reversible or non-reversible mapping later (but keep it deterministic).
- **API Key Security**: AdminApi requires secure API key storage (environment variables, Azure Key Vault, etc.)
- **Certificate Authentication**: AgentApi requires valid client certificates for all secret operations
- **Value Masking**: Secret values, metadata, and expiration dates are never returned in list endpoints
- **Scope Resolution**: AgentApi automatically resolves scopes from agent context; AdminApi requires explicit scope parameters


