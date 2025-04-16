# Conversations

Conversations are the way external world communicate with agent workflows. Agents can have multiple channels of communication such as email, chat, voice, sms, etc.

## Information Model

A thread can have multiple messages.

### Thread (conversation_thread)

- Id
- WorkflowId (composite key)
- TenantId (composite key)
- ParticipantId (composite key)
- CreatedAt
- UpdatedAt
- Status (active, archived, closed)
- Metadata (flexible field for additional properties)

### Message (conversation_message)

- Id
- ThreadId (composite key)
- Channel
- ChannelKey (The identifiable information of the participant. E.g. email address, phone number, etc.)
- CreatedAt
- Direction (inbound or outbound)
- Content (structured based on channel type)
- Status (sent, delivered, read)
- Metadata (flexible field for additional properties)
- Logs (list of events related to the message)

### Message Log (conversation_message_log)

- Timestamp
- Event (created, sent, delivered, read, failed)
- Details (context-specific information)

## APIs

### Thread Management

#### GET api/agent/conversations/threads

List all conversation threads, optionally filtered by workflow or participant.

Query Parameters:

- workflowId (optional): Filter by workflow ID
- participantId (optional): Filter by participant ID
- page (optional): Pagination page number
- pageSize (optional): Number of items per page

#### GET api/agent/conversations/threads/{threadId}

Get details of a specific thread including its messages.

#### PUT api/agent/conversations/threads/{threadId}

Update thread metadata or status.

Payload:

```json
{
  "status": "archived",
  "metadata": {
    "priority": "high",
    "category": "support"
  }
}
```

#### DELETE api/agent/conversations/threads/{threadId}

Archive or delete a thread.

### Message Management

#### POST api/agent/conversations/inbound

Create an inbound message, automatically creating a thread if needed.

Payload:

```json
{
  "workflowId": "workflow_123",
  "participantId": "participant_123",
  "channel": "email",
  "channelKey": "participant_123@example.com",
  "content": {
    "mailId": "mail_123",
    "subject": "Hello, how are you?",
    "body": "Hello, how are you?",
    "cc": ["test@example.com"],
    "bcc": ["test2@example.com"],
    "attachments": [
      {
        "filename": "test.txt",
        "contentType": "text/plain",
        "contentId": "attachment_123",
        "data": "base64-encoded-data"
      }
    ]
  },
  "metadata": {
    "source": "email_server",
    "priority": "normal"
  }
}
```

### Webhook Management

#### POST api/agent/conversations/outbound/webhook

Register a webhook to receive outbound messages.

Payload:

```json
{
  "url": "https://example.com/webhook",
  "secret": "your_secret_key_for_hmac_signing",
  "events": ["message.created", "message.updated"]
}
```

When there is an outbound message, the webhook will be triggered by posting a message to the url. The payload will be as follows:

```json
{
  "threadId": "thread_123",
  "tenantId": "tenant_123",
  "workflowId": "workflow_123",
  "participantId": "participant_123",
  "channel": "email",
  "channelKey": "participant_123@example.com",
  "content": {
    "subject": "Re: Your inquiry",
    "body": "Thank you for your message. Here's the information you requested..."
  },
  "direction": "outbound",
  "metadata": {
    "priority": "high"
  },
  "timestamp": "2023-01-01T12:00:00Z",
  "signature": "hmac-sha256-signature"
}
```

## Error Handling

All APIs will return appropriate HTTP status codes:

- 200: Success
- 400: Bad Request (with validation errors)
- 401: Unauthorized
- 403: Forbidden
- 404: Not Found
- 500: Internal Server Error

Error response format:

```json
{
  "error": {
    "code": "invalid_request",
    "message": "The request was invalid",
    "details": [
      {
        "field": "content",
        "issue": "Content is required for email channel"
      }
    ]
  }
}
```

## Integration with Workflow System

When new inbound messages are received, the following occurs:

1. The message is stored in the database
2. A signal is sent to the corresponding workflow
3. The workflow can then process the message and generate a response

### Workflow Signal

```json
{
  "signalName": "InboundConversationMessage",
  "threadId": "thread_123",
  "messageId": "message_123",
  "tenantId": "tenant_123",
  "workflowId": "workflow_123",
  "channel": "email",
  "channelKey": "participant_123@example.com",
  "participantId": "participant_123",
  "receivedAt": "2023-01-01T12:00:00Z",
  "content": {
    "subject": "Re: Your inquiry",
    "body": "Thank you for your message. Here's the information you requested..."
  },
  "direction": "inbound",
  "metadata": {
    "priority": "high"
  }
}
```

The workflow can then retrieve the message details and respond appropriately, using the outbound message API.
