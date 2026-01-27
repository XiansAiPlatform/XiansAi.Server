# Knowledge

## GET

/api/v1/admin/tenants/{tenantId}/knowledge

Params:

- tenantId
- agentName
- activationName

calls KnowledgeService.GetLatestAll(string agent) and return the knowledge tree

## Override

/api/v1/admin/tenants/{tenantId}/knowledge/{knowledgeId}/override

Query Parameters:

- targetLevel
- activationName (optional)

If the knowledge in focus is a system level knowledge (SystemScoped=true & tenantId=null) then we allow 'tenant' & 'activation' level overrides.

If its a tenant level knowledge (tenantId={tenantId} and activationName=null) we allow overriding at activation level.

If requesting for a tenant level override, we will create a copy of the knowledge with the given tenantId.

If activation level override is requested, the new knowledge copy should have both tenantId and activationName.

## Delete all versions

DELETE
/api/v1/admin/tenants/{tenantId}/knowledge/{name}/{level}/versions
Delete all versions of knowledge by name

Deletes all versions of a knowledge item with a specific name and level.

Parameters:

- level (tenant|activation)
- tenantId *
- name *
- agentName *
- activationName (only if the level is activation)

If tenant level, delete all knowledge where tenantId is {tenantId} for given agentId
If activation level, delete all knowledge where tenantId is {tenantId} for given agentId and activationName
