# Server-Sent Events (SSE) Implementation for UserApi

## Overview

This implementation adds Server-Sent Events (SSE) capability to the UserApi, allowing clients to subscribe to real-time outbound messages from workflows without using WebSockets. The SSE endpoint provides a lightweight, HTTP-based streaming solution that works well with existing HTTP infrastructure.

## Architecture

### Components

1. **MessageEventPublisher** (`Features/UserApi/Services/MessageEventPublisher.cs`)
   - Central event publisher that broadcasts message events to SSE subscribers
   - Thread-safe implementation using event handlers
   - Handles multiple concurrent subscribers with error isolation

2. **SSE Endpoint** (`Features/UserApi/Endpoints/MessagingEndpoints.cs`)
   - New GET endpoint at `/api/user/messaging/events`
   - Streams real-time message events in Server-Sent Events format
   - Supports filtering by workflow, participant, tenant, and scope

3. **MongoChangeStreamService Integration**
   - Updated to publish events to `IMessageEventPublisher`
   - Maintains existing SignalR functionality while adding SSE support
   - Publishes both message and metadata events

### Event Flow

```
MongoDB Change Stream → MongoChangeStreamService → MessageEventPublisher → SSE Clients
                    ↓
                SignalR Hubs (existing functionality preserved)
```

## API Usage

### Endpoint

```
GET /api/user/messaging/events?workflow={workflowId}&participantId={participantId}&tenantId={tenantId}&apikey={apikey}
```

### Parameters

- `workflow` (required): Workflow identifier or workflow type
- `participantId` (required): Participant identifier
- `tenantId` (required): Tenant identifier for authentication
- `apikey` (required): API key for authentication
- `scope` (optional): Filter messages by scope

### Response Format

The endpoint returns Server-Sent Events with the following event types:

#### Connection Event

``` json
event: connected
data: {"message":"Connected to message stream","workflowId":"...","participantId":"...","tenantId":"...","timestamp":"..."}
```

#### Chat Event

``` json
event: chat
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Chat","text":"Hello","data":{},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Data Event

``` json
event: data
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Data","text":null,"data":{"key":"value"},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Handoff Event

``` json
event: handoff
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Handoff","text":"Transferred to agent","data":{},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Heartbeat Event

``` json
event: heartbeat
data: {"timestamp":"...","subscriberCount":1}
```

## Client Implementation Example

### JavaScript/TypeScript

```javascript
const eventSource = new EventSource('/api/user/messaging/events?workflow=myworkflow&participantId=user123&tenantId=tenant1&apikey=sk-Xnai-...');

eventSource.addEventListener('connected', (event) => {
    console.log('Connected to SSE stream:', JSON.parse(event.data));
});

eventSource.addEventListener('chat', (event) => {
    const message = JSON.parse(event.data);
    console.log('Received chat message:', message);
    // Handle chat message
});

eventSource.addEventListener('data', (event) => {
    const dataMessage = JSON.parse(event.data);
    console.log('Received data message:', dataMessage);
    // Handle data message
});

eventSource.addEventListener('handoff', (event) => {
    const handoffMessage = JSON.parse(event.data);
    console.log('Received handoff message:', handoffMessage);
    // Handle handoff message
});

eventSource.addEventListener('heartbeat', (event) => {
    const heartbeat = JSON.parse(event.data);
    console.log('Heartbeat:', heartbeat.timestamp);
});

eventSource.onerror = (error) => {
    console.error('SSE connection error:', error);
};

// Clean up when done
// eventSource.close();
```

### Python

```python
import sseclient
import json
import requests

url = '/api/user/messaging/events'
params = {
    'workflow': 'myworkflow',
    'participantId': 'user123',
    'tenantId': 'tenant1',
    'apikey': 'sk-Xnai-...'
}

response = requests.get(url, params=params, stream=True)
client = sseclient.SSEClient(response)

for event in client.events():
    data = json.loads(event.data)
    
    if event.event == 'connected':
        print(f"Connected: {data['message']}")
    elif event.event == 'chat':
        print(f"New chat message: {data['text']}")
    elif event.event == 'data':
        print(f"New data message: {data['data']}")
    elif event.event == 'handoff':
        print(f"Handoff message: {data['text']}")
    elif event.event == 'heartbeat':
        print(f"Heartbeat at {data['timestamp']}")
```

## Features

### Filtering and Security

- **Tenant Isolation**: Only messages for the authenticated tenant are streamed
- **Workflow/Participant Filtering**: Only messages matching the specified workflow and participant
- **Scope Filtering**: Optional filtering by message scope
- **Authentication**: Uses the same API key authentication as other UserApi endpoints

### Connection Management

- **Automatic Cleanup**: Subscriptions are automatically removed when clients disconnect
- **Heartbeat**: Periodic heartbeat events (every 30 seconds) to detect disconnected clients
- **Error Handling**: Robust error handling with graceful degradation

### Performance Considerations
- **Event Filtering**: Messages are filtered at the publisher level to reduce unnecessary processing
- **Memory Efficient**: Uses event-driven architecture to avoid polling
- **Scalable**: Supports multiple concurrent SSE connections

## Comparison with SignalR

| Feature | SSE | SignalR |
|---------|-----|---------|
| Protocol | HTTP/1.1, HTTP/2 | WebSocket, Server-Sent Events, Long Polling |
| Browser Support | Native EventSource API | Requires SignalR client library |
| Firewall/Proxy | HTTP-friendly | May require WebSocket support |
| Bidirectional | No (Server → Client only) | Yes |
| Message Types | String/JSON | Binary + JSON |
| Reconnection | Native browser handling | SignalR client handles |
| Authentication | Query parameters, Headers | Connection negotiation |

## Configuration

The SSE functionality is automatically enabled when the `MessageEventPublisher` is registered in the DI container. No additional configuration is required.

### Service Registration

```csharp
// In UserApiConfiguration.cs
builder.Services.AddSingleton<IMessageEventPublisher, MessageEventPublisher>();
```

## Monitoring and Diagnostics

### Subscriber Count
The heartbeat events include the current subscriber count, which can be used for monitoring:

```json
{"timestamp":"2024-01-01T00:00:00Z","subscriberCount":5}
```

### Logging
The implementation includes comprehensive logging at various levels:
- Connection events (Info level)
- Message publishing (Debug level)
- Error conditions (Warning/Error level)

## Troubleshooting

### Common Issues

1. **Connection Drops**: 
   - Check network connectivity and proxy settings
   - Verify API key authentication
   - Monitor heartbeat events

2. **Missing Messages**:
   - Verify workflow and participant parameters
   - Check tenant isolation settings
   - Ensure MongoDB change streams are functioning

3. **High Memory Usage**:
   - Monitor subscriber count via heartbeat events
   - Implement client-side connection management
   - Consider connection limits if needed

### Browser Limitations

- **Connection Limits**: Browsers typically limit 6 SSE connections per domain
- **CORS**: Ensure proper CORS configuration for cross-origin requests
- **EventSource Polyfills**: May be needed for older browsers

## Future Enhancements

1. **Message History Replay**: Option to replay recent messages on connection
2. **Connection Limits**: Configurable per-tenant connection limits
3. **Message Filtering**: Advanced filtering options (message types, date ranges)
4. **Compression**: Support for gzip compression on event streams
5. **Metrics**: Detailed metrics collection for monitoring 