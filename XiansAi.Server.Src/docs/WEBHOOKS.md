# Webhooks

Webhooks are used to notify the agents when a certain event occurs.

## Webhook URL Format

```
POST <server-url>/api/user/webhooks/{workflow}/{methodName}
Authorization: Bearer <apikey>
```

- `workflow` should be either the WorkflowId or the WorkflowType.
- `methodName` should be equal to the name of the Temporal Update method.
- Authentication: send the API key in the `Authorization: Bearer <apikey>` header. The
  tenant is derived from the authenticated key — no `tenantId` query parameter is required.

### Deprecated authentication (still accepted, will log a warning)

For backward compatibility, the API key may also be passed as a query parameter:

```
POST <server-url>/api/user/webhooks/{workflow}/{methodName}?apikey={apikey}
```

This pattern is **deprecated** and should not be used in new integrations. Query-string
credentials leak into reverse-proxy access logs, CDN logs, browser history, and `Referer`
headers, and they are visible to anything that can see the URL. The server logs a
deprecation warning every time a request authenticates this way.

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
