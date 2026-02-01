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
