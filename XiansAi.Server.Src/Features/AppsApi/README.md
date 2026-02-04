# AppsApi - External Platform Integrations

## Overview

AppsApi enables bidirectional messaging between XiansAi agents and external platforms (Slack, MS Teams, Outlook, etc.) through a unified integration framework.

## Features

✅ **Bidirectional Messaging**: Send and receive messages from external platforms  
✅ **Automatic Routing**: Responses automatically route back to the correct platform  
✅ **Auto-Preservation**: Origin and metadata automatically preserved from incoming messages  
✅ **Multi-Platform**: Support for Slack, MS Teams, Outlook (extensible)  
✅ **Multi-Tenant**: Full tenant isolation and security  
✅ **Secure**: Webhook signature verification, encrypted credentials  
✅ **Admin API**: Full CRUD operations for managing integrations  

## Quick Start

### 1. Create Integration

```http
POST /api/v1/admin/tenants/{tenantId}/integrations
{
  "platformId": "slack",
  "name": "Support Bot",
  "agentName": "SupportAgent",
  "activationName": "LiveSupport",
  "configuration": {
    "signingSecret": "your-slack-signing-secret",
    "incomingWebhookUrl": "https://hooks.slack.com/services/...",
    "botToken": "xoxb-..."
  },
  "mappingConfig": {
    "participantIdSource": "userId",
    "scopeSource": "channelId"
  }
}
```

### 2. Configure Platform

Use the returned `webhookUrl` in your platform's webhook settings.

### 3. Agent Integration

Agent workflows automatically receive and respond to messages:

```csharp
// Incoming messages arrive via standard signal
[Signal("inbound_chat_or_data")]
public async Task HandleIncomingMessageAsync(InboundMessagePayload payload)
{
    // payload.Origin = "app:slack:{integrationId}"
    // payload.Data.slack contains { channel, threadTs, userId, ... }
    
    var response = await ProcessMessage(payload.Text);
    
    // Ultra-simple response - everything else is automatic!
    await SendOutboundMessage(new ChatOrDataRequest
    {
        WorkflowId = workflowId,
        ParticipantId = payload.ParticipantId,
        Text = response
        // Origin auto-populated ✨
        // Slack metadata auto-populated ✨
    });
}
```

## Architecture

### Components

```
/Features/AppsApi/
├── Models/
│   ├── AppIntegration.cs       - Integration entity & DTOs
│   └── SlackModels.cs          - Slack-specific models
├── Repositories/
│   └── AppIntegrationRepository.cs - MongoDB operations
├── Services/
│   ├── AppIntegrationService.cs    - Business logic
│   └── AppMessageRouterService.cs  - Outbound routing (background service)
├── Handlers/
│   └── SlackWebhookHandler.cs      - Slack event processing
├── Endpoints/
│   └── AppWebhookEndpoints.cs      - Public webhook endpoints
└── Configuration/
    └── AppsApiConfiguration.cs     - DI registration
```

### Message Flow

**Incoming (Platform → Agent):**
```
Slack → /api/apps/slack/events/{id} → SlackWebhookHandler 
→ MessageService → Agent Workflow
```

**Outgoing (Agent → Platform):**
```
Agent → MessageService → MongoDB → MongoChangeStreamService 
→ MessageEventPublisher → AppMessageRouterService 
→ SlackWebhookHandler → Slack API
```

## Supported Platforms

| Platform | Status | Incoming | Outgoing | Features |
|----------|--------|----------|----------|----------|
| Slack | ✅ Complete | Events API | Webhook/Bot API | Messages, Threads, Mentions |
| MS Teams | 🚧 Planned | - | - | - |
| Outlook | 🚧 Planned | - | - | - |
| Generic Webhook | ✅ Complete | HTTP POST | - | Basic messaging |

## Admin API Endpoints

### Integration Management

- `GET /api/v1/admin/tenants/{tenantId}/integrations` - List all
- `GET /api/v1/admin/tenants/{tenantId}/integrations/{id}` - Get one
- `POST /api/v1/admin/tenants/{tenantId}/integrations` - Create
- `PUT /api/v1/admin/tenants/{tenantId}/integrations/{id}` - Update
- `DELETE /api/v1/admin/tenants/{tenantId}/integrations/{id}` - Delete
- `POST .../integrations/{id}/enable` - Enable
- `POST .../integrations/{id}/disable` - Disable
- `POST .../integrations/{id}/test` - Test configuration
- `GET .../integrations/{id}/webhook-url` - Get webhook URL

### Public Webhook Endpoints

- `POST /api/apps/{platformId}/events/{integrationId}` - Generic endpoint
- `POST /api/apps/slack/events/{integrationId}` - Slack Events API
- `POST /api/apps/slack/interactive/{integrationId}` - Slack Interactive

## Configuration

### Required Settings

```json
// appsettings.json or environment variables
{
  "AppsApi": {
    "BaseUrl": "https://your-server.com"  // For webhook URL generation
  }
}
```

### Platform Configuration

**Slack:**
- `signingSecret` (required) - For webhook verification
- `incomingWebhookUrl` (optional) - For outbound messages (simpler)
- `botToken` (optional) - For outbound messages (more features)

**MS Teams:**
- `appId` (required)
- `appPassword` (required)

**Outlook:**
- `clientId` (required)
- `clientSecret` (required)
- `tenantId` (required)

## Auto-Preservation Magic ✨

The system automatically preserves context from incoming messages:

1. **Origin Preservation**: Routes responses back to the same platform
2. **Metadata Preservation**: Includes channel, thread, user info
3. **Thread Continuity**: Maintains conversation threading
4. **Zero Configuration**: Works out of the box for agents

### What Agents Get:

**Incoming Message:**
```json
{
  "text": "Hello bot",
  "participantId": "U1234567890",
  "data": {
    "slack": {
      "channel": "C1234567890",
      "threadTs": "1234567890.123",
      "userId": "U1234567890"
    }
  },
  "origin": "app:slack:69836dfb..."
}
```

**Agent Response (minimal code):**
```json
{
  "text": "Hello back!",
  "participantId": "U1234567890"
  // Everything else auto-populated!
}
```

**Saved with Auto-Preservation:**
```json
{
  "text": "Hello back!",
  "participantId": "U1234567890",
  "data": { "slack": { ... } },  // ← Auto-copied!
  "origin": "app:slack:69836dfb..."  // ← Auto-copied!
}
```

## Documentation

- **[Architecture Guide](./architecture.md)** - System design and components
- **[Slack Integration Guide](./slackapp.md)** - Detailed Slack implementation and testing

## Security

- ✅ Webhook signature verification (HMAC-SHA256 for Slack)
- ✅ Encrypted credential storage
- ✅ Tenant isolation
- ✅ Rate limiting ready
- ✅ Replay attack prevention (timestamp validation)

## Monitoring

Watch for these log entries:

```
[AppMessageRouterService] App Message Router Service started
[SlackWebhookHandler] Processing Slack webhook for app instance {id}
[AppMessageRouterService] Routing outgoing message to slack integration {id}
[SlackWebhookHandler] Successfully sent message to Slack
```

## Troubleshooting

**Integration created but messages not routing:**
- Check `AppMessageRouterService started` in logs
- Verify integration `isEnabled: true`
- Check origin is being set on messages

**Slack not receiving outbound messages:**
- Verify `incomingWebhookUrl` or `botToken` is configured
- Check for "No Slack channel found" warnings
- Ensure incoming message had Slack metadata

**"WorkflowId must start with tenantId" error:**
- Fixed via automatic tenant context setting
- Webhook endpoints now set tenant context from integration

See detailed troubleshooting in [slackapp.md](./slackapp.md).

---

**Status**: Production Ready  
**Version**: 1.0  
**Last Updated**: 2026-02-04
