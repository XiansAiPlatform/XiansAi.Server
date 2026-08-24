# Messaging Endpoints

## topics

/api/v1/admin/tenants/{tenantId}/messaging/topics

Query Params:

- agentName
- activationName
- participantId
- workflowType (optional - must be a built-in conversational workflow when provided, or "Supervisor Workflow" for backward compatibility. When omitted, the agent's unique built-in workflow is used, falling back to Supervisor Workflow)

When this is called we need to take distinct scopes in message threads for

tenantId= {tenantId}
workflowId= {tenantId}:{agentName}:{workflowType}:{activationName}
participantId= {participantId}

## history

/api/v1/admin/tenants/{tenantId}/messaging/history

Query Params:

- agentName
- activationName
- participantId
- workflowType (optional - must be a built-in conversational workflow when provided, or "Supervisor Workflow" for backward compatibility. When omitted, the agent's unique built-in workflow is used, falling back to Supervisor Workflow)
- topic (optional - return messages with null in scope)

## send

POST /api/v1/admin/tenants/{tenantId}/messaging/send

Send a message to a specific agent activation.

Body:

- agentName (required)
- activationName (required)
- participantId (required)
- text (required)
- workflowType (optional - must be a built-in conversational workflow when provided, or "Supervisor Workflow" for backward compatibility. When omitted, the agent's unique built-in workflow is used, falling back to Supervisor Workflow)
- data (optional)
- topic (optional - stored as scope in message thread)
- type (optional - 'Chat' or 'Data', defaults to 'Chat')
- requestId (optional - if not provided, a GUID will be generated)
- hint (optional)
- authorization (optional - can also be provided via Authorization header)
- origin (optional)

The endpoint constructs the workflowId as follows:

workflowId={tenantId}:{agentName}:{workflowType}:{activationName}

## listen

GET /api/v1/admin/tenants/{tenantId}/messaging/listen

Subscribe to real-time message events using Server-Sent Events (SSE).

Query Parameters:

- agentName (required)
- activationName (required)
- participantId (required)
- workflowType (optional - must be a built-in conversational workflow when provided, or "Supervisor Workflow" for backward compatibility. When omitted, the agent's unique built-in workflow is used, falling back to Supervisor Workflow)
- heartbeatSeconds (optional - default: 60, range: 1-300)

The endpoint constructs the workflowId as follows:

workflowId={tenantId}:{agentName}:{workflowType}:{activationName}

Behavior:

- Automatically creates a conversation thread if it doesn't exist
- Streams all messages for the specified agent activation and participant
- Sends periodic heartbeat events to keep the connection alive


How to test:

Listen: 

curl -N -H "Authorization: Bearer $ADMIN_API_KEY" \
  "http://localhost:5005/api/v1/admin/tenants/default/messaging/listen?agentName=Order%20Manager%20Agent&activationName=Order%20Manager%20Agent%20-%20Remote%20Peafowl&participantId=hasith@gmail.com"


send messages:

curl -X POST "http://localhost:5005/api/v1/admin/tenants/default/messaging/send" \
  -H "Authorization: Bearer $ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "agentName": "Order Manager Agent",
    "activationName": "Order Manager Agent - Remote Peafowl",
    "participantId": "hasith@gmail.com",
    "text": "Test message from curl",
    "type": "Chat"
  }'