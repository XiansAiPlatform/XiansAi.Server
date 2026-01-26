# Admin Stats Service

## Implementation Status: ✅ COMPLETED

### Overview

The Admin Stats Service provides aggregated statistics across multiple domains (tasks, messaging) for administrative monitoring and reporting. It combines data from Temporal workflows and MongoDB collections to provide a comprehensive view of tenant activity.

### Endpoint

#### GET /api/v1/admin/tenants/{tenantId}/stats

**Status:** ✅ Implemented

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `participantId` (optional): Filter statistics by participant user ID (query parameter)

**Response:**

```json
{
  "tasks": {
    "pending": 10,
    "completed": 45,
    "timedOut": 2,
    "cancelled": 3,
    "total": 60
  },
  "messages": {
    "activeUsers": 15,
    "totalMessages": 234
  }
}
```

**Task Statistics:**
- `pending`: Number of tasks currently running
- `completed`: Number of tasks that completed successfully
- `timedOut`: Number of tasks that exceeded their timeout
- `cancelled`: Number of tasks that were cancelled or terminated
- `total`: Total number of tasks in the date range

**Messaging Statistics:**
- `activeUsers`: Number of unique users (participants) who sent messages
- `totalMessages`: Total number of messages sent in the date range

### Examples

```bash
# Get stats for a specific date range
GET /api/v1/admin/tenants/default/stats?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z

# Get stats filtered by participant
GET /api/v1/admin/tenants/default/stats?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z&participantId=user@example.com

# Get stats for the last 7 days
GET /api/v1/admin/tenants/default/stats?startDate=2025-01-18T00:00:00Z&endDate=2025-01-25T23:59:59Z
```

### Implementation Details

**Service Layer:**
- `AdminStatsService` (Shared/Services/AdminStatsService.cs) - Aggregates statistics from multiple sources
- Delegates to `AdminTaskService` for task statistics from Temporal
- Uses `ConversationRepository` for messaging statistics from MongoDB

**Repository Layer:**
- `ConversationRepository.GetMessagingStatsAsync()` - Retrieves messaging statistics using MongoDB aggregation
- Efficiently counts total messages and distinct active users in a single operation

**Endpoint Layer:**
- `AdminStatsEndpoints` (Features/AdminApi/Endpoints/AdminStatsEndpoints.cs) - HTTP endpoint definition
- Implements authorization via AdminEndpointAuthPolicy
- Provides comprehensive OpenAPI documentation

**Data Sources:**
- Task statistics: Temporal workflows (via AdminTaskService)
- Messaging statistics: MongoDB conversation_message collection

### Error Handling

- Validates required parameters (tenantId, startDate, endDate)
- Validates date range (startDate cannot be after endDate)
- Returns zero messaging stats if messaging query fails (to prevent full request failure)
- Comprehensive logging at all levels
