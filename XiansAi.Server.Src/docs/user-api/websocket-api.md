# WebSocket API

Real-time bidirectional communication using SignalR WebSockets.

## Endpoints

- `wss://api.example.com/ws/chat` - Main chat hub
- `wss://api.example.com/ws/tenant/chat` - Tenant-specific hub

## Connection

### With API Key
```
wss://api.example.com/ws/chat?tenantId=tenant-id&apikey=sk-key
```

### With JWT Token
```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("wss://api.example.com/ws/chat?tenantId=tenant-id", {
    accessTokenFactory: () => "your-jwt-token"
  })
  .build();
```

## Client → Server Methods

### SendInboundMessage
Send message to workflow.

```javascript
await connection.invoke("SendInboundMessage", {
  requestId: "req-123",
  participantId: "user-123", 
  workflow: "customer-support",
  type: "Chat",
  text: "Hello, I need help"
}, "Chat");
```

### SubscribeToAgent
Subscribe to workflow notifications.

```javascript
await connection.invoke("SubscribeToAgent", "customer-support", "user-123", "tenant-id");
```

### UnsubscribeFromAgent
Unsubscribe from workflow.

```javascript
await connection.invoke("UnsubscribeFromAgent", "customer-support", "user-123", "tenant-id");
```

### GetThreadHistory
Request conversation history.

```javascript
await connection.invoke("GetThreadHistory", "customer-support", "user-123", 0, 50);
```

### GetScopedThreadHistory
Request scoped conversation history.

```javascript
await connection.invoke("GetScopedThreadHistory", "customer-support", "user-123", 0, 50, "support");
```

## Server → Client Events

### ReceiveChat
Receive chat messages from agents.

```javascript
connection.on("ReceiveChat", (message) => {
  console.log("Agent:", message.text);
});
```

### ReceiveData
Receive data messages from agents.

```javascript
connection.on("ReceiveData", (message) => {
  console.log("Data:", message.data);
});
```

### ReceiveHandoff
Receive handoff notifications.

```javascript
connection.on("ReceiveHandoff", (message) => {
  console.log("Handoff:", message.data);
});
```

### ThreadHistory
Receive conversation history.

```javascript
connection.on("ThreadHistory", (messages) => {
  console.log("History:", messages);
});
```

### InboundProcessed
Notification that message was processed.

```javascript
connection.on("InboundProcessed", (threadId) => {
  console.log("Processed:", threadId);
});
```

### Error
Receive error notifications.

```javascript
connection.on("Error", (error) => {
  console.error("Error:", error);
});
```

## Message Format

### Request Message
```json
{
  "requestId": "req-123",
  "participantId": "user-123",
  "workflow": "customer-support", 
  "type": "Chat",
  "text": "Hello",
  "data": {"priority": "high"}
}
```

### Response Message
```json
{
  "id": "msg-456",
  "createdAt": "2024-01-15T10:30:00Z",
  "direction": "Outgoing",
  "messageType": "Chat",
  "text": "Hello! How can I help?",
  "participantId": "user-123",
  "workflowId": "tenant:customer-support",
  "workflowType": "customer-support"
}
```

## Complete Example

```javascript
// Connect
const connection = new signalR.HubConnectionBuilder()
  .withUrl("wss://api.example.com/ws/chat?tenantId=tenant-id&apikey=sk-key")
  .build();

// Setup event handlers
connection.on("ReceiveChat", (message) => {
  console.log("Agent:", message.text);
});

// Connect and subscribe
await connection.start();
await connection.invoke("SubscribeToAgent", "customer-support", "user-123", "tenant-id");

// Send message
await connection.invoke("SendInboundMessage", {
  requestId: "req-123",
  participantId: "user-123",
  workflow: "customer-support",
  type: "Chat", 
  text: "Hello"
}, "Chat");
```

## Error Handling

```javascript
connection.onclose((error) => {
  console.log("Connection closed:", error);
});

connection.on("Error", (error) => {
  console.error("Hub error:", error);
});
``` 