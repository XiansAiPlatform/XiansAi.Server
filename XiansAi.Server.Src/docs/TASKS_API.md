# Tasks API Documentation

## Overview

The Tasks API provides REST endpoints for managing human-in-the-loop task workflows in the WebAPI feature. Tasks are implemented as Temporal workflows that allow human participants to review, update, complete, or reject work items.

## Architecture

### Components

1. **TaskService** (`Services/TaskService.cs`)
   - Core service implementing task management logic
   - Interfaces with Temporal workflows for task operations
   - Handles querying, updating, completing, and rejecting tasks

2. **TaskEndpoints** (`Endpoints/TaskEndpoints.cs`)
   - REST API endpoint mappings
   - Route: `/api/client/tasks`
   - Requires valid tenant and authorization

3. **Models** (`Models/TaskModels.cs`)
   - `TaskInfoResponse`: Task information response model
   - `UpdateDraftRequest`: Request model for updating task drafts
   - `RejectTaskRequest`: Request model for rejecting tasks
   - `PaginatedTasksResponse`: Paginated list response

## API Endpoints

### 1. Get Task by ID

```
GET /api/client/tasks?workflowId={workflowId}
```

Retrieves detailed information about a specific task.

**Parameters:**
- `workflowId` (query): Workflow ID of the task (can contain complex identifiers with colons, URLs, etc.)

**Response:**
```json
{
  "taskId": "string",
  "workflowId": "string",
  "runId": "string",
  "title": "string",
  "description": "string",
  "currentDraft": "string",
  "participantId": "string",
  "status": "Running|Completed|Failed|...",
  "isCompleted": false,
  "error": "string",
  "startTime": "2024-01-01T00:00:00Z",
  "closeTime": "2024-01-01T00:00:00Z",
  "metadata": {}
}
```

### 2. List Tasks (Paginated)

```
GET /api/client/tasks/list?pageSize=20&pageToken=1&agent=Platform&participantId=user123
```

Retrieves a paginated list of tasks with optional filtering.

**Query Parameters:**
- `pageSize` (optional): Number of items per page (default: 20, max: 100)
- `pageToken` (optional): Continuation token from previous response
- `agent` (optional): Filter by agent name
- `participantId` (optional): Filter by participant/user ID

**Response:**
```json
{
  "tasks": [...],
  "nextPageToken": "2",
  "pageSize": 20,
  "hasNextPage": true,
  "totalCount": null
}
```

**Filtering:**
- Always filtered by tenant ID (automatic)
- Uses Temporal search attributes for agent and participantId filtering
- Workflow type is automatically filtered to "Platform:Task Workflow"

### 3. Update Task Draft

```
PUT /api/client/tasks/draft?workflowId={workflowId}
```

Updates the draft work content for a task.

**Parameters:**
- `workflowId` (query): Workflow ID of the task

**Request Body:**
```json
{
  "updatedDraft": "string"
}
```

**Response:**
```json
{
  "message": "Draft updated successfully"
}
```

### 4. Complete Task

```
POST /api/client/tasks/complete?workflowId={workflowId}
```

Marks a task as completed, signaling the workflow to continue.

**Parameters:**
- `workflowId` (query): Workflow ID of the task

**Response:**
```json
{
  "message": "Task completed successfully"
}
```

### 5. Reject Task

```
POST /api/client/tasks/reject?workflowId={workflowId}
```

Rejects a task with a reason, signaling the workflow that the task cannot be completed.

**Parameters:**
- `workflowId` (query): Workflow ID of the task

**Request Body:**
```json
{
  "rejectionMessage": "string"
}
```

**Response:**
```json
{
  "message": "Task rejected successfully"
}
```

## Workflow Integration

### Workflow ID Format

Tasks use a specific workflow ID format:
```
{tenantId}:Platform:Task Workflow:{taskId}
```

### Workflow Signals

The service sends the following signals to task workflows:
- `UpdateDraft`: Updates the draft work
- `CompleteTask`: Marks task as complete
- `RejectTask`: Rejects the task with a message

### Workflow Queries

- `GetTaskInfo`: Queries current task information

## Search Attributes

Tasks use the following Temporal search attributes for filtering:
- `tenantId`: Tenant identifier (always filtered)
- `agent`: Agent name (optional filter)
- `userId`: Participant/user ID (optional filter, mapped to participantId)

## Memo Fields

Task workflows store additional metadata in memo:
- `taskTitle`: Task title
- `taskDescription`: Task description
- `tenantId`: Tenant identifier
- `agent`: Agent name
- `userId`: User/participant identifier
- `systemScoped`: System-scoped flag

## Authentication & Authorization

All endpoints require:
- Valid tenant context (`RequiresValidTenant()`)
- User authorization (`RequireAuthorization()`)
- Tenant ID is automatically extracted from the authenticated context

## Error Handling

Common error responses:
- `400 Bad Request`: Invalid input or task not found
- `401 Unauthorized`: Missing or invalid authentication
- `403 Forbidden`: Insufficient permissions
- `500 Internal Server Error`: Server-side error

## Example Usage

### Creating and Managing a Task

1. **List available tasks:**
   ```bash
   GET /api/client/tasks/list?participantId=user123&pageSize=10
   ```

2. **Get task details:**
   ```bash
   GET /api/client/tasks?workflowId=hasith:Company%20Research%20Agent:Content%20Discovery%20Workflow:task-123
   ```

3. **Update draft work:**
   ```bash
   PUT /api/client/tasks/draft?workflowId=hasith:Company%20Research%20Agent:Content%20Discovery%20Workflow:task-123
   Content-Type: application/json
   
   {
     "updatedDraft": "Updated work in progress..."
   }
   ```

4. **Complete the task:**
   ```bash
   POST /api/client/tasks/complete?workflowId=hasith:Company%20Research%20Agent:Content%20Discovery%20Workflow:task-123
   ```

   Or reject it:
   ```bash
   POST /api/client/tasks/reject?workflowId=hasith:Company%20Research%20Agent:Content%20Discovery%20Workflow:task-123
   Content-Type: application/json
   
   {
     "rejectionMessage": "Cannot complete due to missing information"
   }
   ```

## Implementation Notes

- Tasks are implemented as Temporal child workflows
- The service follows the same patterns as `WorkflowFinderService`
- Pagination uses simple page tokens (page numbers)
- All task operations are tenant-scoped for security
- Constants for memo keys are defined in `Shared/Utils/Constants.cs`

## Related Files

- Service: `XiansAi.Server.Src/Features/WebApi/Services/TaskService.cs`
- Endpoints: `XiansAi.Server.Src/Features/WebApi/Endpoints/TaskEndpoints.cs`
- Models: `XiansAi.Server.Src/Features/WebApi/Models/TaskModels.cs`
- Configuration: `XiansAi.Server.Src/Features/WebApi/Configuration/WebApiConfiguration.cs`
- Constants: `XiansAi.Server.Src/Shared/Utils/Constants.cs`

## Task Workflow Implementation

The task workflows are implemented in the `XiansAi.Lib` library:
- `TaskWorkflowService`: Service for creating and managing tasks
- `TaskWorkflow`: Workflow implementation
- `TaskWorkflowOptions`: Child workflow options
- Models: `TaskInfo`, `TaskWorkflowRequest`, `TaskWorkflowResult`

