# REST API

HTTP endpoints for fire-and-forget messaging, synchronous conversations, and history retrieval.

## Endpoints

### POST /api/user/rest/send

Fire-and-forget message delivery.

**Query Parameters:**

- `workflow` (required) - Workflow identifier
- `type` (required) - Message type: `Chat`, `Data`, or `Handoff`
- `participantId` (required) - Participant identifier
- `tenantId` (required) - Tenant identifier
- `apikey` (required if no JWT) - API key
- `requestId` (optional) - Correlation ID
- `text` (optional) - Text content for Chat messages

**Request Body:** JSON data (optional for Chat, required for Data/Handoff)

**Example:**

```bash
curl -X POST "https://api.example.com/api/user/rest/send?workflow=customer-support&type=Chat&participantId=user-123&tenantId=tenant-id&apikey=sk-key" \
  -H "Content-Type: application/json" \
  -d '{"priority": "high"}'
```

### POST /api/user/rest/converse

Send message and wait for response synchronously.

**Query Parameters:**

- Same as `/send` plus:
- `timeoutSeconds` (optional) - Timeout in seconds (default: 60, max: 300)

**Response:** Array of messages

**Example:**

```bash
curl -X POST "https://api.example.com/api/user/rest/converse?workflow=faq-bot&type=Chat&participantId=user-123&tenantId=tenant-id&apikey=sk-key&text=What are your hours?" \
  -H "Content-Type: application/json"
```

### GET /api/user/rest/history

Retrieve conversation history.

**Query Parameters:**

- `workflow` (required) - Workflow identifier
- `participantId` (required) - Participant identifier
- `tenantId` (required) - Tenant identifier
- `apikey` (required if no JWT) - API key
- `page` (optional) - Page number (default: 1)
- `pageSize` (optional) - Page size (default: 50)
- `scope` (optional) - Message scope filter

**Example:**

```bash
curl "https://api.example.com/api/user/rest/history?workflow=customer-support&participantId=user-123&tenantId=tenant-id&apikey=sk-key&page=1&pageSize=20"
```

## Message Formats

### Chat Message

```json
{
  "text": "Hello, I need help",
  "metadata": {"priority": "normal"}
}
```

### Data Message

```json
{
  "operation": "process_order",
  "orderId": "ORD-123",
  "items": [{"sku": "ITEM-001", "quantity": 2}]
}
```

### Response Format

```json
{
  "success": true,
  "data": [
    {
      "id": "msg-123",
      "createdAt": "2024-01-15T10:30:00Z",
      "direction": "Outgoing",
      "messageType": "Chat",
      "text": "Hello! How can I help you?",
      "participantId": "user-123",
      "workflowId": "tenant:customer-support",
      "workflowType": "customer-support"
    }
  ]
}
```

## Error Responses

```json
{
  "success": false,
  "error": "Authentication failed",
  "statusCode": 401
}
```

## Authentication Examples

### With API Key

```bash
curl "https://api.example.com/api/user/rest/send?apikey=sk-key&tenantId=tenant-id&..."
```

### With JWT Token

```bash
curl -H "Authorization: Bearer jwt-token" \
     "https://api.example.com/api/user/rest/send?tenantId=tenant-id&..."
```
