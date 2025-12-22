# Server-Sent Events (SSE) Implementation for WebAPI Messaging

## Overview

This implementation adds Server-Sent Events (SSE) capability to the WebAPI messaging endpoints, enabling real-time message updates for conversation threads without polling. The SSE endpoint provides a lightweight, HTTP-based streaming solution for receiving messages in real-time.

## Architecture

### Components

1. **WebApiSSEStreamHandler** (`Features/WebApi/Utils/WebApiSSEStreamHandler.cs`)
   - Handles SSE stream connections for thread-based messaging
   - Filters messages by thread ID and tenant ID
   - Manages connection lifecycle with heartbeats and cleanup

2. **SSE Endpoint** (`Features/WebApi/Endpoints/SseEndpoints.cs`)
   - New GET endpoint at `/api/client/messaging/threads/{threadId}/events`
   - Streams real-time message events in Server-Sent Events format
   - Supports configurable heartbeat intervals

3. **MessageEventPublisher Integration**
   - Reuses the existing `IMessageEventPublisher` from UserApi
   - Shared event publisher for both UserApi and WebApi SSE streams
   - Published by `MongoChangeStreamService` when messages are inserted/updated

### Event Flow

```
MongoDB Change Stream → MongoChangeStreamService → MessageEventPublisher → WebAPI SSE Clients
                    ↓
                SignalR Hubs (existing functionality preserved)
                    ↓
                UserApi SSE (existing functionality preserved)
```

## API Usage

### Endpoint

```
GET /api/client/messaging/threads/{threadId}/events?heartbeatSeconds={seconds}
```

### Parameters

- `threadId` (required, route): Thread identifier
- `heartbeatSeconds` (optional, query): Heartbeat interval (1-300 seconds, default: 5)

### Authentication

- Requires valid JWT token (same as other WebAPI endpoints)
- Validates tenant ID from JWT claims
- Thread-level authorization applies

### Response Format

The endpoint returns Server-Sent Events with the following event types:

#### Connection Event

```json
event: connected
data: {"message":"Connected to thread message stream","threadId":"...","tenantId":"...","timestamp":"..."}
```

#### Chat Event

```json
event: Chat
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Chat","text":"Hello","data":{},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Data Event

```json
event: Data
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Data","text":null,"data":{"key":"value"},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Handoff Event

```json
event: Handoff
data: {"id":"...","threadId":"...","workflowId":"...","participantId":"...","direction":"Outgoing","messageType":"Handoff","text":"Transferred to agent","data":{},"hint":null,"scope":null,"requestId":"...","createdAt":"...","createdBy":"..."}
```

#### Heartbeat Event

```json
event: heartbeat
data: {"timestamp":"...","subscriberCount":5}
```

## Client Implementation

### React/JavaScript Example

```javascript
import { useEffect, useState } from 'react';
import { useMessagingApi } from './services/messaging-api';

function ChatComponent({ threadId }) {
    const [messages, setMessages] = useState([]);
    const messagingApi = useMessagingApi();

    useEffect(() => {
        if (!threadId) return;

        // Start SSE stream
        const streamPromise = messagingApi.streamThreadMessages(threadId, (event) => {
            console.log('Received SSE event:', event);

            if (event.event === 'connected') {
                console.log('Connected to message stream');
            } else if (event.event === 'Chat' || event.event === 'Data' || event.event === 'Handoff') {
                // Add new message to state
                setMessages(prev => {
                    const exists = prev.some(m => m.id === event.data.id);
                    if (exists) return prev;
                    return [event.data, ...prev];
                });
            } else if (event.event === 'heartbeat') {
                console.log('Heartbeat received');
            }
        });

        // Cleanup on unmount
        return () => {
            // The stream will automatically close when component unmounts
        };
    }, [threadId, messagingApi]);

    return (
        <div>
            {messages.map(msg => (
                <div key={msg.id}>{msg.text}</div>
            ))}
        </div>
    );
}
```

### Custom Hook for SSE Streaming

The implementation includes a custom React hook `useMessageStreaming` that encapsulates the SSE connection logic:

```javascript
import useMessageStreaming from '../hooks/useMessageStreaming';

const messageStreaming = useMessageStreaming({
    threadId: selectedThreadId,
    onMessageReceived: (messageData) => {
        console.log('New message:', messageData);
        // Update UI with new message
    },
    messagingApi,
    onError: (error) => {
        console.error('Streaming error:', error);
    }
});

// Start streaming
messageStreaming.startStreaming(threadId);

// Stop streaming
messageStreaming.stopStreaming();
```

## Features

### Filtering and Security

- **Tenant Isolation**: Only messages for the authenticated tenant are streamed
- **Thread Filtering**: Only messages for the specific thread ID
- **Authentication**: Uses JWT authentication from WebAPI
- **Authorization**: Same authorization rules as other messaging endpoints

### Connection Management

- **Automatic Cleanup**: Subscriptions are automatically removed when clients disconnect
- **Heartbeat**: Periodic heartbeat events (configurable, default 5 seconds)
- **Error Handling**: Robust error handling with graceful degradation
- **Reconnection**: Clients can reconnect if connection is lost

### Performance Considerations

- **Event Filtering**: Messages are filtered by thread ID to reduce unnecessary processing
- **Memory Efficient**: Uses event-driven architecture with shared publisher
- **Scalable**: Supports multiple concurrent SSE connections per thread
- **Shared Infrastructure**: Reuses existing MessageEventPublisher from UserApi

## Comparison with Polling

| Feature | SSE | Polling |
|---------|-----|---------|
| Network Efficiency | High (persistent connection) | Low (repeated requests) |
| Latency | Very low (instant) | High (polling interval) |
| Server Load | Low (one connection) | High (N requests per minute) |
| Client Complexity | Medium | Low |
| Browser Support | Modern browsers | All browsers |
| Scalability | High | Poor |

## Migration from Polling

The implementation replaces the previous polling mechanism (`useMessagePolling`) with real-time SSE streaming:

**Before (Polling):**
- Used `useMessagePolling` hook with exponential backoff
- Fetched messages every 3-10 seconds
- High server load with many clients
- 3-10 second delay for new messages

**After (SSE):**
- Uses `useMessageStreaming` hook with persistent connection
- Messages arrive in real-time (< 100ms)
- Lower server load (one persistent connection vs many requests)
- Automatic reconnection on connection loss

## Configuration

No additional configuration is required. The SSE functionality is automatically enabled when:

1. `MessageEventPublisher` is registered in UserApi configuration (already done)
2. `MongoChangeStreamService` is running (already configured)
3. WebAPI SSE endpoints are mapped (done in `WebApiConfiguration.cs`)

## Monitoring and Diagnostics

### Subscriber Count

The heartbeat events include the current subscriber count across all SSE connections:

```json
{"timestamp":"2024-01-01T00:00:00Z","subscriberCount":15}
```

### Logging

The implementation includes comprehensive logging:
- Connection events (Info level)
- Message streaming (Debug level)
- Error conditions (Warning/Error level)

Example logs:
```
[Info] WebAPI SSE connection established for thread abc123, tenant tenant1
[Debug] Sending WebAPI SSE event Chat for message msg456 to thread abc123
[Info] WebAPI SSE connection closed for thread abc123, tenant tenant1
```

## Troubleshooting

### Common Issues

1. **Connection Drops**:
   - Check network connectivity
   - Verify JWT token is valid
   - Monitor heartbeat events
   - Check browser console for errors

2. **Missing Messages**:
   - Verify thread ID is correct
   - Check tenant isolation settings
   - Ensure MongoDB change streams are functioning
   - Verify message is for the correct thread

3. **High Memory Usage**:
   - Monitor subscriber count via heartbeat events
   - Implement client-side connection management
   - Check for connection leaks (unclosed streams)

### Browser Limitations

- **Connection Limits**: Browsers typically limit 6 SSE connections per domain
- **CORS**: Ensure proper CORS configuration for cross-origin requests
- **Mobile Browsers**: Some mobile browsers may close connections when app is backgrounded

## Testing

To test the SSE implementation:

1. **Start the application** and navigate to the messaging page
2. **Select a thread** - SSE connection should automatically establish
3. **Send a message** - verify it appears in real-time without refresh
4. **Check browser console** - look for "Connected to message stream" log
5. **Monitor heartbeats** - should see heartbeat events every 5 seconds
6. **Test reconnection** - disable network briefly and verify reconnection
7. **Check server logs** - verify SSE connection and message events are logged

## Future Enhancements

1. **Connection Pooling**: Share SSE connections across multiple threads
2. **Message History Replay**: Option to replay recent messages on connection
3. **Connection Limits**: Configurable per-tenant connection limits
4. **Advanced Filtering**: Filter by message type or metadata
5. **Compression**: Support for gzip compression on event streams
6. **Metrics**: Detailed metrics collection for monitoring and alerting

