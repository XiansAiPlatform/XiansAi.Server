# SSE API

Server-Sent Events for real-time notifications and status updates.

## Endpoint

**GET** `/api/user/sse/events`

Stream real-time message events using Server-Sent Events (SSE).

## Parameters

**Query Parameters:**

- `workflow` (required) - Workflow identifier
- `participantId` (required) - Participant identifier  
- `tenantId` (required) - Tenant identifier
- `apikey` (required if no JWT) - API key
- `scope` (optional) - Message scope filter
- `heartbeatSeconds` (optional) - Heartbeat interval (1-300 seconds, default: 5)

## Connection Examples

### With API Key

```bash
curl -N "https://api.example.com/api/user/sse/events?workflow=customer-support&participantId=user-123&tenantId=tenant-id&apikey=sk-key&heartbeatSeconds=30"
```

### With JWT Token

```bash
curl -N -H "Authorization: Bearer jwt-token" \
  "https://api.example.com/api/user/sse/events?workflow=customer-support&participantId=user-123&tenantId=tenant-id"
```

### JavaScript EventSource

```javascript
// With API Key
const eventSource = new EventSource(
  'https://api.example.com/api/user/sse/events?workflow=customer-support&participantId=user-123&tenantId=tenant-id&apikey=sk-key'
);

// Event listeners
eventSource.addEventListener('Chat', (event) => {
  const message = JSON.parse(event.data);
  console.log('Chat:', message.text);
});

eventSource.addEventListener('Data', (event) => {
  const message = JSON.parse(event.data);
  console.log('Data:', message.data);
});

eventSource.addEventListener('Handoff', (event) => {
  const message = JSON.parse(event.data);
  console.log('Handoff:', message.data);
});

eventSource.addEventListener('heartbeat', (event) => {
  const data = JSON.parse(event.data);
  console.log('Heartbeat:', data.subscriberCount, 'subscribers');
});
```

## Event Types

### Chat Events

``` json
event: Chat
data: {"id":"msg-123","createdAt":"2024-01-15T10:30:00Z","direction":"Outgoing","messageType":"Chat","text":"Hello! How can I help?","participantId":"user-123","workflowId":"tenant:customer-support","workflowType":"customer-support"}
```

### Data Events

``` json
event: Data
data: {"id":"msg-124","createdAt":"2024-01-15T10:31:00Z","direction":"Outgoing","messageType":"Data","data":{"status":"processed","result":"success"},"participantId":"user-123","workflowId":"tenant:order-processor","workflowType":"order-processor"}
```

### Handoff Events

``` json
event: Handoff
data: {"id":"msg-125","createdAt":"2024-01-15T10:32:00Z","direction":"Outgoing","messageType":"Handoff","text":"Transferring to specialist","data":{"fromAgent":"ai-bot","toAgent":"human-agent","reason":"escalation"},"participantId":"user-123","workflowId":"tenant:escalation-manager","workflowType":"escalation-manager"}
```

### Heartbeat Events

``` json
event: heartbeat
data: {"timestamp":"2024-01-15T10:33:00Z","subscriberCount":5}
```

### Connection Events

``` json
event: connected
data: {"timestamp":"2024-01-15T10:30:00Z"}

event: disconnected  
data: {"timestamp":"2024-01-15T10:35:00Z","reason":"Connection lost"}
```

## Error Handling

### Connection Errors

```javascript
eventSource.onerror = (error) => {
  console.error('SSE connection error:', error);
  
  // Reconnect after delay
  setTimeout(() => {
    eventSource = new EventSource(url);
  }, 5000);
};
```

### Authentication Errors

HTTP 401 response when authentication fails.

### Rate Limiting

HTTP 429 response when rate limited.

## Complete Example

```javascript
function connectToSSE(workflow, participantId, apiKey) {
  const url = `https://api.example.com/api/user/sse/events?workflow=${workflow}&participantId=${participantId}&tenantId=tenant-id&apikey=${apiKey}&heartbeatSeconds=15`;
  
  const eventSource = new EventSource(url);
  
  // Handle different event types
  eventSource.addEventListener('Chat', (event) => {
    const message = JSON.parse(event.data);
    displayChatMessage(message);
  });
  
  eventSource.addEventListener('Data', (event) => {
    const message = JSON.parse(event.data);
    processDataMessage(message);
  });
  
  eventSource.addEventListener('Handoff', (event) => {
    const message = JSON.parse(event.data);
    handleHandoff(message);
  });
  
  eventSource.addEventListener('heartbeat', (event) => {
    const data = JSON.parse(event.data);
    updateConnectionStatus(data.subscriberCount);
  });
  
  // Error handling
  eventSource.onerror = (error) => {
    console.error('SSE error:', error);
  };
  
  return eventSource;
}

// Usage
const sse = connectToSSE('customer-support', 'user-123', 'sk-key');
```

## Browser Compatibility

- **Modern browsers**: Full EventSource support
- **Legacy browsers**: Polyfill required (`eventsource` npm package)
- **JWT limitations**: Some browsers don't support custom headers with EventSource

For JWT authentication in browsers with header limitations, the API automatically falls back to `access_token` query parameter.
