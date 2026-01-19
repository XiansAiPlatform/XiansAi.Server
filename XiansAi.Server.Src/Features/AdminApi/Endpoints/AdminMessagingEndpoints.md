# Messaging Endpoints

## topics

/api/v1/admin/tenants/{tenantId}/messaging/topics

Query Params:

- agentName
- activationName
- participantId

When this is called we need to take distinct scopes in message threads for

tenantId= {tenantId}
workflowId= {tenantId}:{agentName}:Supervisor Workflow:{activationName}
participantId= {participantId}

## history

/api/v1/admin/tenants/{tenantId}/messaging/history

Query Params:

- agentName
- activationName
- participantId
- topic (optional - return messages with null in scope)

## send

POST /api/v1/admin/tenants/{tenantId}/messaging/send

Send a message to a specific agent activation.

Body:

- agentName (required)
- activationName (required)
- participantId (required)
- text (required)
- data (optional)
- topic (optional - stored as scope in message thread)
- type (optional - 'Chat' or 'Data', defaults to 'Chat')
- requestId (optional - if not provided, a GUID will be generated)
- hint (optional)
- authorization (optional - can also be provided via Authorization header)
- origin (optional)

The endpoint constructs the workflowId as follows:

workflowId={tenantId}:{agentName}:Supervisor Workflow:{activationName}

## listen

GET /api/v1/admin/tenants/{tenantId}/messaging/listen

Subscribe to real-time message events using Server-Sent Events (SSE).

Query Parameters:

- agentName (required)
- activationName (required)
- participantId (required)
- heartbeatSeconds (optional - default: 60, range: 1-300)

The endpoint constructs the workflowId as follows:

workflowId={tenantId}:{agentName}:Supervisor Workflow:{activationName}

Behavior:

- Automatically creates a conversation thread if it doesn't exist
- Streams all messages for the specified agent activation and participant
- Sends periodic heartbeat events to keep the connection alive


How to test:

Listen: 

curl -N -H "Authorization: Bearer sk-Xnai-0DzZq_c6iIXz64aNoST-qCYK8wR-IY8S_T_X5Z49IGo" \
  "http://localhost:5005/api/v1/admin/tenants/default/messaging/listen?agentName=Order%20Manager%20Agent&activationName=Order%20Manager%20Agent%20-%20Remote%20Peafowl&participantId=hasith@gmail.com"


send messages:

curl -X POST "http://localhost:5005/api/v1/admin/tenants/default/messaging/send" \
  -H "Authorization: Bearer sk-Xnai-0DzZq_c6iIXz64aNoST-qCYK8wR-IY8S_T_X5Z49IGo" \
  -H "Content-Type: application/json" \
  -d '{
    "agentName": "Order Manager Agent",
    "activationName": "Order Manager Agent - Remote Peafowl",
    "participantId": "hasith@gmail.com",
    "text": "Test message from curl",
    "type": "Chat"
  }'