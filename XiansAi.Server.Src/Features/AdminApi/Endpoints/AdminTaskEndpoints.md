# AdminTaskEndpoints

## Implementation Status: ✅ COMPLETED

### Endpoints Implemented

#### 1. GET /api/v1/admin/tenants/{tenantId}/tasks

List all tasks for a tenant with optional filtering.

**Query Parameters:**
- `pageSize` (optional): Number of items per page (default: 20, max: 100)
- `pageToken` (optional): Token for pagination (page number)
- `agentName` (optional): Filter by agent name (exact match)
- `activationName` (optional): Filter by activation name (maps to idPostfix search attribute)
- `participantId` (optional): Filter by participant user ID
- `status` (optional): Filter by execution status (e.g., Running, Completed, Failed)

**Response:** Paginated list of tasks with metadata including:
- Task information (title, description, participant)
- Workflow details (status, start/close times)
- Available actions and performed action
- Admin-specific fields (agent name, activation name, tenant ID)

#### 2. GET /api/v1/admin/tenants/{tenantId}/tasks/{workflowId}

Get detailed task information by workflow ID.

**Response:** Complete task details including initial/final work content and metadata.

#### 3. PUT /api/v1/admin/tenants/{tenantId}/tasks/{workflowId}/draft

Update the draft work for a task.

**Request Body:**
- `updatedDraft` (string, required): The new draft content

**Response:** Success message confirming the draft was updated.

#### 4. POST /api/v1/admin/tenants/{tenantId}/tasks/{workflowId}/actions

Perform an action on a task with an optional comment.

**Request Body:**
- `action` (string, required): The action to perform (should match one of the task's available actions)
- `comment` (string, optional): Optional comment explaining the action

**Response:** Success message confirming the action was performed.

### Temporal Search Attributes

Tasks are fetched from Temporal workflows using these search attributes:
- `tenantId`: Tenant identifier
- `agent`: Agent name (e.g., "Order Manager Agent")
- `idPostfix`: Activation identifier (e.g., "Order Manager Agent - Remote Peafowl")
- `userId`: Participant user ID (e.g., "hasith@gmail.com")
- `WorkflowType`: Workflow type pattern (e.g., "{agentName}:Task Workflow")

### Implementation Details

**Service Layer:**
- `AdminTaskService` (Shared/Services/AdminTaskService.cs) - Reusable service for admin task operations
- Handles pagination, filtering, and Temporal workflow queries
- Maps workflow executions to admin task response models

**Endpoint Layer:**
- `AdminTaskEndpoints` (Features/AdminApi/Endpoints/AdminTaskEndpoints.cs) - HTTP endpoint definitions
- Implements authorization via AdminEndpointAuthPolicy
- Provides OpenAPI documentation

**Configuration:**
- Service registered in `AdminApiConfiguration.AddAdminApiServices()`
- Endpoints mapped in `AdminApiConfiguration.MapAdminApiVersion()`

### Code Reuse

The `UpdateDraft` and `PerformAction` methods are shared between AdminApi and WebApi:
- Implemented in `AdminTaskService` (Shared/Services)
- WebApi's `TaskService` delegates to `AdminTaskService` for these operations
- Eliminates code duplication and ensures consistent behavior

### Example Usage

```bash
# List all tasks for a tenant
GET /api/v1/admin/tenants/default/tasks

# List tasks with filters
GET /api/v1/admin/tenants/default/tasks?agentName=Order%20Manager%20Agent&pageSize=10

# List tasks by activation
GET /api/v1/admin/tenants/default/tasks?activationName=Order%20Manager%20Agent%20-%20Remote%20Peafowl

# Get specific task
GET /api/v1/admin/tenants/default/tasks/{workflowId}

# Update draft for a task
PUT /api/v1/admin/tenants/default/tasks/{workflowId}/draft
Content-Type: application/json
{
  "updatedDraft": "Updated work content..."
}

# Perform action on a task
POST /api/v1/admin/tenants/default/tasks/{workflowId}/actions
Content-Type: application/json
{
  "action": "approve",
  "comment": "Looks good, approved!"
}
```
