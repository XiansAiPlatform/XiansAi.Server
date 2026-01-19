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
