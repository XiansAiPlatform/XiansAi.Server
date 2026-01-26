# Agent Multi-tenancy

When an agent is created, if the SystemScoped is set to true we create a record in a mongodb collection 'templates'. If not we create a record in a collection 'deployments'.

templates can be deployed to a tenant createing a deployment

deployment can be used to create activations

templates:

- _id
- agentName
- createdBy
- createdAt

deployments:

- _id
- agentName
- tenantId
- createdBy
- createdAt

agents:
flow_definitions:

## Knowledge

On 'templates' knowledge record has tenantId=null
On 'deployments' knowledge record has tenantId=<tenantId>
On 'activations' knowledge record has tenantId=<tenantId> & actibationName=<activationName>
