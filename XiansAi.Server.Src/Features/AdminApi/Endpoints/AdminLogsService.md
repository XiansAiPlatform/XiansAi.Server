# Admin Logs Service

## Implementation Status: ✅ COMPLETED

### Overview

The Admin Logs Service provides query access to workflow execution logs for administrative monitoring, debugging, and auditing. It retrieves log entries from MongoDB that are created by agents during workflow execution via the AgentAPI.

### Endpoint

#### GET /api/v1/admin/tenants/{tenantId}/logs

**Status:** ✅ Implemented

**Parameters:**

- `tenantId` (required): Tenant identifier (path parameter)
- `agentName` (optional): Filter by agent name (exact match, query parameter)
- `activationName` (optional): Filter by activation name (query parameter)
- `participantId` (optional): Filter by participant user ID (query parameter)
- `workflowId` (optional): Filter by specific workflow ID (query parameter)
- `workflowType` (optional): Filter by workflow type (query parameter)
- `logLevel` (optional): Filter by log level (Error, Warning, Information, Debug, Trace, query parameter)
- `startDate` (optional): Start of date range for log filtering (ISO 8601 format, query parameter)
- `endDate` (optional): End of date range for log filtering (ISO 8601 format, query parameter)
- `pageSize` (optional): Number of items per page (default: 20, max: 100, query parameter)
- `page` (optional): Page number for pagination (default: 1, query parameter)

**Response:**

```json
{
  "logs": [
    {
      "id": "507f1f77bcf86cd799439011",
      "tenantId": "default",
      "createdAt": "2025-01-25T10:30:00Z",
      "level": "Information",
      "message": "Task processing started",
      "workflowId": "default:Order Manager Agent:Task Workflow:uuid",
      "workflowRunId": "run-uuid",
      "workflowType": "Order Manager Agent:Task Workflow",
      "agent": "Order Manager Agent",
      "activation": "Order Manager Agent - Remote Peafowl",
      "participantId": "user@example.com",
      "properties": {
        "customKey": "customValue"
      },
      "exception": null,
      "updatedAt": null
    }
  ],
  "totalCount": 150,
  "page": 1,
  "pageSize": 20,
  "totalPages": 8
}
```

**Log Object Fields:**
- `id`: Unique log entry identifier (MongoDB ObjectId)
- `tenantId`: Tenant identifier
- `createdAt`: Timestamp when log was created (UTC)
- `level`: Log severity level (Error, Warning, Information, Debug, Trace)
- `message`: Log message text
- `workflowId`: Temporal workflow ID associated with this log
- `workflowRunId`: Temporal workflow run ID (optional)
- `workflowType`: Type of workflow (e.g., "Agent Name:Task Workflow")
- `agent`: Agent name that generated the log
- `activation`: Activation name (optional, e.g., "Agent Name - Animal Name")
- `participantId`: User ID of the participant (optional)
- `properties`: Additional custom properties (optional)
- `exception`: Exception details if log is for an error (optional)
- `updatedAt`: Timestamp of last update (optional)

### Examples

```bash
# Get all logs for a tenant
GET /api/v1/admin/tenants/default/logs?pageSize=20&page=1

# Get logs filtered by agent
GET /api/v1/admin/tenants/default/logs?agentName=Order%20Manager%20Agent

# Get logs by activation
GET /api/v1/admin/tenants/default/logs?activationName=Order%20Manager%20Agent%20-%20Remote%20Peafowl

# Get error logs only
GET /api/v1/admin/tenants/default/logs?logLevel=Error

# Get logs for a specific date range
GET /api/v1/admin/tenants/default/logs?startDate=2025-01-01T00:00:00Z&endDate=2025-01-31T23:59:59Z

# Get logs for specific participant
GET /api/v1/admin/tenants/default/logs?participantId=user@example.com

# Combine multiple filters
GET /api/v1/admin/tenants/default/logs?agentName=Order%20Manager%20Agent&logLevel=Error&startDate=2025-01-20T00:00:00Z&pageSize=50
```

### Implementation Details

**Service Layer:**
- `AdminLogsService` (Shared/Services/AdminLogsService.cs) - Handles log retrieval with comprehensive filtering
- Validates all input parameters (pagination, date ranges)
- Delegates to LogRepository for data access
- Returns paginated response with metadata

**Repository Layer:**
- `LogRepository.GetAdminLogsAsync()` (Features/WebApi/Repositories/LogRepository.cs) - Extended method for admin log queries
- Supports optional filtering on all fields (unlike GetFilteredLogsAsync which requires agent)
- Uses MongoDB filters for efficient querying
- Optimized with proper indexes

**Endpoint Layer:**
- `AdminLogsEndpoints` (Features/AdminApi/Endpoints/AdminLogsEndpoints.cs) - HTTP endpoint definition
- Implements authorization via AdminEndpointAuthPolicy
- Provides comprehensive OpenAPI documentation with examples
- Registered in `AdminApiConfiguration.MapAdminApiVersion()`

**Data Model:**
- Uses existing `Log` model from `Features/WebApi/Models/Log.cs`
- Collection: `logs` in MongoDB
- All fields available for filtering and response

**Related Code:**
- **Log Creation**: `Features/AgentApi/Endpoints/LogsEndpoints.cs` (POST endpoints)
- **Similar Endpoints**: `AdminTaskEndpoints`, `AdminStatsEndpoints`

### Database Indexes

The following MongoDB indexes are configured in `mongodb-indexes.yaml` for optimal query performance:

- `tenant_id_1_created_at_-1` - For basic tenant + date range queries
- `tenant_id_1_agent_1_created_at_-1` - For tenant + agent + date range queries
- `tenant_id_1_activation_1_created_at_-1` - For tenant + activation + date range queries
- `tenant_id_1_workflow_id_1_created_at_-1` - For tenant + workflow ID + date range queries
- `tenant_id_1_agent_1_participant_id_1` - For tenant + agent + participant queries
- `tenant_id_1_workflow_type_1_level_1` - For tenant + workflow type + level queries
- `level_1_tenant_id_1_created_at_-1_workflow_type_1_autocreated` - Multi-field compound index
- `logs_ttl_created_at` - TTL index for automatic log expiration (15 days)

### Error Handling

- Validate required parameters (tenantId)
- Validate date range if provided (startDate cannot be after endDate)
- Validate pagination parameters (page >= 1, pageSize between 1-100)
- Return appropriate HTTP status codes:
  - 200: Success with logs
  - 400: Invalid parameters
  - 401: Unauthorized (no admin credentials)
  - 500: Internal server error

### Authorization

- Requires `AdminEndpointAuthPolicy`
- Admin API key authentication required
- Cross-tenant access allowed for admin users
