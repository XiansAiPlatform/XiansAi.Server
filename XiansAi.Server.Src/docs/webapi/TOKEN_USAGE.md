# Token Usage Management Endpoints

These endpoints expose token-usage telemetry and configuration for the Manager UI and automation clients. All endpoints live under `/api/client/usage` and require Tenant Admin (or Sys Admin) authorization.

## Endpoints

### `GET /api/client/usage/status`
- Returns the rolling window usage snapshot for the current tenant/user.
- Query parameters:
  - `tenantId` (optional, sys-admin only) – inspect another tenant.
  - `userId` (optional) – inspect a specific user; defaults to the current caller.

### `GET /api/client/usage/limits`
- Lists tenant-level and user-level limits for the tenant.
- Query parameters:
  - `tenantId` (optional, sys-admin only) – inspect another tenant.

### `POST /api/client/usage/limits`
- Creates or updates a limit.
- Body:
  ```json
  {
    "tenantId": "tenant-a",   // optional for current tenant
    "userId": "user-123",     // omit for tenant-level limit
    "maxTokens": 50000,
    "windowSeconds": 7200,
    "enabled": true
  }
  ```

### `DELETE /api/client/usage/limits/{id}`
- Deletes a tenant/user override by its identifier.

## Response Shapes

### Usage Status
```json
{
  "enabled": true,
  "maxTokens": 200000,
  "tokensUsed": 1200,
  "tokensRemaining": 198800,
  "windowSeconds": 86400,
  "windowStart": "2024-11-26T00:00:00Z",
  "windowEndsAt": "2024-11-27T00:00:00Z",
  "isExceeded": false
}
```

### Usage Limit
```json
{
  "id": "6563f2c8ce...",
  "tenantId": "tenant-a",
  "userId": "user-123",      // null for tenant-level limits
  "maxTokens": 50000,
  "windowSeconds": 7200,
  "enabled": true,
  "effectiveFrom": "2024-11-26T00:00:00Z",
  "updatedAt": "2024-11-26T10:05:00Z",
  "updatedBy": "admin@tenant-a"
}
```

## Authorization
- Tenant Admins can manage their own tenant.
- Sys Admins can manage any tenant via the `tenantId` parameter.

## Related Features
- [TOKEN_LIMITING_IMPLEMENTATION_PLAN.md](../TOKEN_LIMITING_IMPLEMENTATION_PLAN.md)
- [TOKEN_LIMITING_PHASES.md](../TOKEN_LIMITING_PHASES.md)

