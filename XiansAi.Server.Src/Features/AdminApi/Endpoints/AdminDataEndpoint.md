# Admin Data Service

AdminAPI endpoints for agent document data. All routes require an Admin API key (`sk-Xnai-…`) with **SysAdmin** or **TenantAdmin**, and the route `{tenantId}` must match the tenant resolved from that key.

Base path: `/api/v1/admin/tenants/{tenantId}/data`

---

### 1. LIST TYPES - GET /api/v1/admin/tenants/{tenantId}/data/schema

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (optional): Filter by specific activation name (query parameter)

**Response:**

```json
{
  "period": {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "filters": {
    "agentName": "CustomerSupportAgent",
    "activationName": null
  },
  "types": ["Companies", "Mails Sent"]
}
```

---

### 2. LIST - GET /api/v1/admin/tenants/{tenantId}/data

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (optional): Filter by specific activation name (query parameter)
- `dataType` (required)
- pagination: `skip`, `limit`

**Response:**

```json
{
  "data": [
    {
      "id": "697e2b40993d83f992f4ab0c",
      "key": "https://www.springhealth.com:https://www.lyrahealth.com:summary:2026-01-31_16-18-08",
      "type": "Companies",
      "agentName": "CustomerSupportAgent",
      "activationName": "email-responder",
      "participantId": "hasithy@99x.io",
      "content": {
        "PeerGroupName": "https://www.springhealth.com",
        "Status": "Completed"
      },
      "metadata": {
        "city": "Oslo"
      },
      "createdAt": "2026-01-31T16:18:08.790Z",
      "updatedAt": null,
      "expiresAt": null
    }
  ],
  "total": 1,
  "skip": 0,
  "limit": 100
}
```

---

### 3. GET /api/v1/admin/tenants/{tenantId}/data/{recordId}

**Parameters:**

- `recordId` (required): The unique identifier of the record (path parameter)

**Response (200):** the same item shape as list.

**Response (404):** record does not exist, or belongs to a different tenant.

---

### 4. CREATE - POST /api/v1/admin/tenants/{tenantId}/data

Creates a new data record. Tenant is taken from the route, never from the body. The agent must exist in that tenant. Create is insert-only; it does not overwrite an existing type+key.

**Body:**

```json
{
  "agentName": "CustomerSupportAgent",
  "dataType": "Companies",
  "key": "acme-2026-01",
  "activationName": "email-responder",
  "participantId": "user@example.com",
  "content": { "Status": "Completed" },
  "metadata": { "city": "Oslo" },
  "expiresAt": null,
  "workflowId": null
}
```

| Field | Required | Notes |
|---|---|---|
| `agentName` | yes | Must exist in the route tenant |
| `dataType` | yes | Stored as document `type` |
| `key` | yes | Unique together with `dataType` within the tenant |
| `content` | yes | Any non-null JSON value |
| `activationName` | no | |
| `participantId` | no | |
| `metadata` | no | |
| `expiresAt` | no | |
| `workflowId` | no | |

**Response (201):** the created record (`AdminDataItemResponse`).

**Response (400):** missing `agentName`, `dataType`, `key`, or `content`.

**Response (404):** agent does not exist in the tenant.

**Response (409):** a record with the same `dataType` + `key` already exists in the tenant.

**Security:**
- Requires admin authorization (`AdminEndpointAuthPolicy`)
- Route tenant must match the API key tenant (`TenantRouteScopeFilter`)
- `tenantId` in a request body is ignored; the route tenant is stamped on the document

---

### 5. UPDATE - PUT /api/v1/admin/tenants/{tenantId}/data/{recordId}

Partially updates an existing data record. `id`, `tenantId`, `agentName` / `agentId`, `createdAt`, and `createdBy` cannot be changed.

**Body** (all fields optional; omitted fields are left unchanged):

```json
{
  "content": { "Status": "Updated" },
  "metadata": { "city": "Bergen" },
  "key": "acme-2026-01",
  "dataType": "Companies",
  "participantId": "user@example.com",
  "activationName": "email-responder",
  "expiresAt": null
}
```

**Response (200):** the updated record.

**Response (404):** record does not exist, or belongs to a different tenant (same “security by obscurity” as delete).

**Response (409):** changing `dataType`/`key` would collide with a different record in the tenant.

**Security:**
- Requires admin authorization
- Route tenant must match the API key tenant
- Record `tenantId` is checked again in the service; a cross-tenant id returns 404
- Extra body fields such as `tenantId` or `agentName` are ignored

---

### 6. DELETE /api/v1/admin/tenants/{tenantId}/data

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `dataType` (required): The specific data type to delete (query parameter)
- `activationName` (optional): Filter by specific activation name (query parameter)

**Response:**

```json
{
  "deletedCount": 25,
  "period": {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "filters": {
    "agentName": "CustomerSupportAgent",
    "activationName": null
  },
  "dataType": "Companies"
}
```

Permanently deletes all data records of the specified type that match the given filters and date range. This operation is irreversible.

**Security:**
- Requires admin authorization
- Respects tenant isolation — users can only delete from their own tenant
- All parameters are validated before deletion
- Returns count of deleted records for verification

---

### 7. DELETE /api/v1/admin/tenants/{tenantId}/data/{recordId}

**Parameters:**

- `recordId` (required): The unique identifier of the record to delete (path parameter)

**Response:**

```json
{
  "deleted": true,
  "recordId": "697e2b40993d83f992f4ab0c",
  "deletedRecord": { }
}
```

Permanently deletes a specific data record by its unique identifier. Returns the complete deleted record for confirmation.

**Security:**
- Requires admin authorization
- Respects tenant isolation
- Returns 404 for non-existent records OR records from different tenants
- All deletion attempts are logged

*Success (200):* `{ "deleted": true, "recordId": "...", "deletedRecord": { ... } }`

*Not Found (404):* `{ "deleted": false, "recordId": "...", "deletedRecord": null }`
