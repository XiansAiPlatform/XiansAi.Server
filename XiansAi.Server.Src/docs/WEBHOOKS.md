# Webhooks

Webhooks are used to notify the agents when a certain event occurs.

## Webhook URL Format

POST <server-url>/api/user/webhooks/{workflow}/{methodName}?tenantId={tenantId}&apikey={apikey}

- `workflow` should either the WorkflowId or the WorkflowType.
- `methodName` should be equal to the name of the Temporal Update method.
- `tenantId` must be a valid tenantId (query parameter).
- `apikey` must be a valid APIKEY of the tenant (query parameter).

## Webhook's Temporal Update Method

```csharp
[Update("method-name")]
public async Task<string> WebhookUpdateMethod(IDictionary<string, string> queryParams, string body)
{
    ...
    return stringResponse
}
```

`method-name` should be equal to the `methodName` in the webhook URL.
`queryParams` is the query parameters in the webhook URL, except for `apikey` and `tenantId`.
`body` is the request body of the webhook.

The return value should be a string which will be sent to the webhook URL caller in response body.

## Implementation

- Endpoint: Features/UserApi/Endpoints/WebhookEndpoints.cs
  - authenticate with APIkey
  - use WorkflowIdentifier to get the WorkflowId and WorkflowType
- Service: Features/UserApi/Services/WebhookService.cs
- Util Class: Shared/Utils/Temporal/UpdateService.cs
  - use NewWorkflowOptions to send the Update with Start
