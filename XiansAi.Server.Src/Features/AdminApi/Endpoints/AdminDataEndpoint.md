# Admin Metrics Service

## Endpoints

### 1. LIST - GET /api/v1/admin/tenants/{tenantId}/data/schema

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (required): Filter by specific activation name (query parameter)

**Response:**

```json
{
  "period": {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "filters": {
    "agentName": "CustomerSupportAgent",
    "activationName": null,
  },
  "types": ["Companies", "Mails Sent"]
}
```

---

### 2. GET /api/v1/admin/tenants/{tenantId}/data

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (required): Filter by specific activation name (query parameter)
- `dataType` (required)
- <pagination params>

**Response:**

```json
[
  {
    "id": "697e2b40993d83f992f4ab0c",
    "key": "https://www.springhealth.com:https://www.lyrahealth.com:summary:2026-01-31_16-18-08",
    "participantId": "hasithy@99x.io",
    "content": {
      "PeerGroupName": "https://www.springhealth.com",
      "PeerUrl": "https://www.lyrahealth.com",
      "NewsPageCount": 2,
      "ProcessedLinkCount": 6,
      "SkippedLinkCount": 20,
      "TotalLinksFound": 26,
      "DiscoveryCompletedAt": "2026-01-31T16:18:08.782048Z",
      "Status": "Completed"
    },
    "metadata": {
      "city": "Oslo"
    },

    "createdAt": "2026-01-31T16:18:08.790Z",
    "updatedAt": null,
    "expiresAt": null,
  },
  {
    ...
  }
]
```

---

### 3. DELETE /api/v1/admin/tenants/{tenantId}/data

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

**Description:**

Permanently deletes all data records of the specified type that match the given filters and date range. This operation is irreversible and should be used with caution.

**Use Cases:**
- Clean up test data from admin dashboards
- Remove outdated or incorrect data processing results  
- Reset agent data for a specific type during development
- Bulk cleanup of agent data based on date ranges

**Security & Safety:**
- Requires admin authorization
- Respects tenant isolation - users can only delete from their own tenant
- All parameters are validated before deletion
- Returns count of deleted records for verification

**Examples:**
- `DELETE /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies`
- `DELETE /api/v1/admin/tenants/{tenantId}/data?startDate=2026-01-01T00:00:00Z&endDate=2026-01-31T23:59:59Z&agentName=CustomerSupportAgent&dataType=Companies&activationName=email-responder`

---

### 4. DELETE /api/v1/admin/tenants/{tenantId}/data/{recordId}

**Parameters:**

- `recordId` (required): The unique identifier of the record to delete (path parameter)

**Response:**

```json
{
  "deleted": true,
  "recordId": "697e2b40993d83f992f4ab0c",
  "deletedRecord": {
    "id": "697e2b40993d83f992f4ab0c",
    "key": "https://www.springhealth.com:https://www.lyrahealth.com:summary:2026-01-31_16-18-08",
    "participantId": "hasithy@99x.io",
    "content": {
      "PeerGroupName": "https://www.springhealth.com",
      "PeerUrl": "https://www.lyrahealth.com",
      "NewsPageCount": 2,
      "ProcessedLinkCount": 6,
      "SkippedLinkCount": 20,
      "TotalLinksFound": 26,
      "DiscoveryCompletedAt": "2026-01-31T16:18:08.782048Z",
      "Status": "Completed"
    },
    "metadata": {
      "city": "Oslo"
    },
    "createdAt": "2026-01-31T16:18:08.790Z",
    "updatedAt": null,
    "expiresAt": null
  }
}
```

**Description:**

Permanently deletes a specific data record by its unique identifier. Returns the complete deleted record for confirmation and audit purposes.

**Use Cases:**
- Remove specific erroneous or test records
- Clean up individual problematic data entries  
- Delete records identified through admin data browsing
- Remove sensitive data that was inadvertently stored

**Security & Safety:**
- Requires admin authorization
- Respects tenant isolation - users can only delete records from their own tenant
- Returns 404 for non-existent records OR records from different tenants (security by obscurity)
- Returns complete deleted record data for confirmation and audit trails
- All deletion attempts are logged for security auditing

**Examples:**
- `DELETE /api/v1/admin/tenants/{tenantId}/data/697e2b40993d83f992f4ab0c`

**Response Scenarios:**

*Success (200):*
```json
{
  "deleted": true,
  "recordId": "697e2b40993d83f992f4ab0c",
  "deletedRecord": { /* complete record data */ }
}
```

*Not Found (404):*
```json
{
  "deleted": false,
  "recordId": "non-existent-id",
  "deletedRecord": null
}
```
