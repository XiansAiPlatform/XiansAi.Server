# AppsApi Architecture - Proxy Apps for Agent Integration

## Overview

The **AppsApi** provides a framework for building proxy applications that enable bidirectional messaging between external platforms (Slack, MS Teams, Outlook, etc.) and XiansAi agent activations. These proxy apps act as middleware, translating platform-specific protocols into the XiansAi messaging system.

## Core Concepts

### 1. Proxy Apps

Proxy apps are platform-specific integrations that:

- Expose public endpoints to receive webhook events from external platforms
- Transform external platform messages into XiansAi message format
- Route messages to appropriate agent activations based on configuration
- Transform XiansAi agent responses back to platform-specific format
- Handle platform-specific authentication and event verification

### 2. Common Interface

All proxy apps implement a common interface (`IAppProxy`) to ensure consistency:

```csharp
public interface IAppProxy
{
    // Platform identifier (e.g., "slack", "msteams", "outlook")
    string PlatformId { get; }
    
    // Receive and process incoming webhook from platform
    Task<AppProxyResult> ProcessIncomingWebhookAsync(
        AppProxyIncomingContext context,
        CancellationToken cancellationToken = default);
    
    // Send outgoing message to platform
    Task<AppProxyResult> SendOutgoingMessageAsync(
        AppProxyOutgoingContext context,
        CancellationToken cancellationToken = default);
    
    // Verify webhook signature/authentication
    Task<bool> VerifyWebhookAuthenticationAsync(
        HttpContext httpContext,
        AppProxyConfiguration config);
}
```

### 3. Data Flow

#### Incoming Flow (Platform → Agent)

```
External Platform (Slack, Teams, etc.)
    ↓ [Webhook]
AppsApi Public Endpoint (/api/apps/{platformId}/webhook/{appInstanceId})
    ↓ [Verification & Parsing]
Platform-Specific Proxy Implementation (SlackProxy, TeamsProxy, etc.)
    ↓ [Transform to ChatOrDataRequest]
MessageService.ProcessIncomingMessage()
    ↓ [Store & Signal]
Agent Workflow (via Temporal Signal)
```

#### Outgoing Flow (Agent → Platform)

```
Agent Workflow
    ↓ [Outbound Webhook/Data Message]
MessageService.ProcessOutgoingMessage()
    ↓ [Message Event Published]
AppsApi Message Subscriber (listening for app-routed messages)
    ↓ [Route by appInstanceId/Origin]
Platform-Specific Proxy Implementation
    ↓ [Transform & Send]
External Platform API (Slack API, Teams API, etc.)
```

## Architecture Components

### 1. Endpoint Layer (`/Features/AppsApi/Endpoints/`)

#### **AppProxyEndpoints.cs**

Public webhook endpoints for receiving events from external platforms.

```csharp
// Generic webhook endpoint for all platforms
POST /api/apps/{platformId}/webhook/{appInstanceId}

// Platform-specific endpoints (optional, for platforms with specific requirements)
POST /api/apps/slack/interactive/{appInstanceId}
POST /api/apps/slack/events/{appInstanceId}
POST /api/apps/msteams/messaging/{appInstanceId}
```

Key responsibilities:

- Route incoming webhooks to appropriate proxy implementation
- Load app instance configuration from repository
- Verify webhook authenticity (signatures, tokens)
- Handle platform-specific event challenges/verifications
- Return appropriate responses to platform

### 2. Proxy Layer (`/Features/AppsApi/Proxies/`)

Platform-specific implementations:

#### **SlackProxy.cs**

- Handles Slack Events API webhooks
- Processes interactive components (buttons, modals)
- Manages Slack-specific message formatting (blocks, attachments)
- Handles OAuth flow and token management
- Verifies request signatures using Slack signing secret

#### **TeamsProxy.cs**

- Handles Microsoft Teams Bot Framework messages
- Processes activities (message, invoke, etc.)
- Manages adaptive cards
- Handles Teams-specific authentication

#### **OutlookProxy.cs**

- Handles Outlook/Exchange webhook notifications
- Processes email messages and calendar events
- Manages Microsoft Graph API interactions
- Handles OAuth and access token refresh

### 3. Service Layer (`/Features/AppsApi/Services/`)

#### **AppProxyService.cs**

Orchestrates proxy operations:

- Discovers and manages registered proxy implementations
- Routes requests to appropriate proxy based on platformId
- Handles common error handling and logging
- Manages retry logic for failed deliveries

#### **AppInstanceService.cs**

Manages app instance configurations:

- CRUD operations for app instances
- Stores platform-specific credentials (tokens, secrets)
- Maps app instances to agent activations
- Handles configuration validation

#### **AppMessageRouterService.cs**

Routes outgoing messages from agents to platforms:

- Subscribes to message events from MessageService
- Filters messages by origin/appInstanceId
- Routes to appropriate proxy implementation
- Handles delivery failures and retries

### 4. Repository Layer (`/Features/AppsApi/Repositories/`)

#### **AppInstanceRepository.cs**

Data persistence for app instances:

```csharp
public class AppInstance
{
    public string Id { get; set; }
    public string TenantId { get; set; }
    public string PlatformId { get; set; } // "slack", "msteams", etc.
    public string Name { get; set; }
    
    // Routing configuration
    public string AgentName { get; set; }
    public string ActivationName { get; set; }
    public string WorkflowId { get; set; } // Auto-built: {tenantId}:{agentName}:Supervisor Workflow:{activationName}
    
    // Platform-specific configuration
    public Dictionary<string, object> Configuration { get; set; }
    // e.g., { "botToken": "xoxb-...", "signingSecret": "...", "teamId": "..." }
    
    // Mapping configuration
    public AppInstanceMappingConfig MappingConfig { get; set; }
    
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AppInstanceMappingConfig
{
    // How to determine participantId from platform message
    public string ParticipantIdSource { get; set; } // "userId", "channelId", "threadId", "custom"
    public string? ParticipantIdCustomField { get; set; }
    
    // How to determine scope/topic
    public string? ScopeSource { get; set; } // "channelName", "channelId", "threadId", "custom", null
    public string? ScopeCustomField { get; set; }
    
    // Default values
    public string? DefaultParticipantId { get; set; }
    public string? DefaultScope { get; set; }
}
```

### 5. Models (`/Features/AppsApi/Models/`)

#### **AppProxyContext.cs**

Request/response context models:

```csharp
public class AppProxyIncomingContext
{
    public required string AppInstanceId { get; set; }
    public required AppInstance AppInstance { get; set; }
    public required HttpContext HttpContext { get; set; }
    public required string RawBody { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public Dictionary<string, string>? QueryParams { get; set; }
}

public class AppProxyOutgoingContext
{
    public required string AppInstanceId { get; set; }
    public required AppInstance AppInstance { get; set; }
    public required ConversationMessage Message { get; set; }
    public required string WorkflowId { get; set; }
    public required string ParticipantId { get; set; }
}

public class AppProxyResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public object? Data { get; set; }
    public int? HttpStatusCode { get; set; }
}
```

#### **AppProxyMessage.cs**

Platform-agnostic message representation:

```csharp
public class AppProxyMessage
{
    public required string Text { get; set; }
    public object? Data { get; set; }
    public string? ParticipantId { get; set; }
    public string? Scope { get; set; }
    public string? ThreadId { get; set; }
    public MessageType Type { get; set; } // Chat, Data, etc.
    public Dictionary<string, object>? Metadata { get; set; }
}
```

## Message Flow Examples

### Example 1: Slack Message to Agent

1. **User sends message in Slack channel**
   ```
   User: @BotName help me with my order #12345
   ```

2. **Slack sends event to webhook**
   ```
   POST /api/apps/slack/webhook/{appInstanceId}
   {
     "type": "event_callback",
     "event": {
       "type": "app_mention",
       "user": "U123456",
       "text": "<@U789> help me with my order #12345",
       "channel": "C987654",
       "ts": "1234567890.123456"
     }
   }
   ```

3. **SlackProxy transforms to ChatOrDataRequest**

   ```csharp
   var chatRequest = new ChatOrDataRequest
   {
       WorkflowId = appInstance.WorkflowId, // "tenant123:SupportAgent:Supervisor Workflow:LiveSupport"
       ParticipantId = "U123456", // Slack user ID
       Text = "help me with my order #12345",
       Data = new {
           slackUserId = "U123456",
           slackChannel = "C987654",
           slackThreadTs = "1234567890.123456",
           originalEvent = eventData
       },
       Scope = "C987654", // Channel as topic
       Origin = "app:slack:{appInstanceId}",
       Type = MessageType.Chat
   };
   ```

4. **MessageService processes and signals workflow**

5. **Agent workflow receives message and processes**

### Example 2: Agent Response to Slack

1. **Agent workflow sends outbound message**

   ```csharp
   var response = new ChatOrDataRequest
   {
       WorkflowId = workflowId,
       ParticipantId = "U123456",
       Text = "I found your order #12345. It's out for delivery!",
       Data = new {
           slackChannel = "C987654",
           slackThreadTs = "1234567890.123456"
       },
       Origin = "app:slack:{appInstanceId}",
       Type = MessageType.Chat
   };
   await messageService.ProcessOutgoingMessage(response, MessageType.Chat);
   ```

2. **AppMessageRouterService intercepts based on Origin pattern**

3. **SlackProxy transforms and sends to Slack API**

   ```csharp
   POST https://slack.com/api/chat.postMessage
   {
     "channel": "C987654",
     "thread_ts": "1234567890.123456",
     "text": "I found your order #12345. It's out for delivery!"
   }
   ```

## Configuration & Setup

### App Instance Creation

Admin creates app instances via AdminApi:

```http
POST /api/v1/admin/tenants/{tenantId}/apps
{
  "platformId": "slack",
  "name": "Support Bot",
  "agentName": "SupportAgent",
  "activationName": "LiveSupport",
  "configuration": {
    "botToken": "xoxb-your-token",
    "signingSecret": "your-signing-secret",
    "appId": "A123456"
  },
  "mappingConfig": {
    "participantIdSource": "userId",
    "scopeSource": "channelId"
  },
  "isActive": true
}
```

Response includes webhook URL:
```json
{
  "id": "app_instance_123",
  "webhookUrl": "https://yourserver.com/api/apps/slack/webhook/app_instance_123",
  "webhookEventUrl": "https://yourserver.com/api/apps/slack/events/app_instance_123"
}
```

Admin configures this URL in Slack App settings.

## Security Considerations

1. **Webhook Verification**
   - Each proxy MUST verify webhook authenticity
   - Slack: Verify request signature using signing secret
   - Teams: Verify Bot Framework signature
   - Outlook: Verify Microsoft Graph subscription validation token

2. **Credential Storage**
   - Store platform credentials (tokens, secrets) encrypted
   - Use Azure Key Vault or similar for production
   - Rotate tokens periodically

3. **Rate Limiting**
   - Apply rate limits per app instance
   - Handle platform-specific rate limits
   - Implement exponential backoff for retries

4. **Multi-tenancy**
   - Strict tenant isolation
   - App instances belong to specific tenant
   - WorkflowId always includes tenantId prefix

## Extensibility

### Adding New Platform Proxy

1. **Create proxy class**
   ```csharp
   public class CustomProxy : IAppProxy
   {
       public string PlatformId => "custom";
       // Implement interface methods
   }
   ```

2. **Register in DI container**
   ```csharp
   services.AddScoped<IAppProxy, CustomProxy>();
   ```

3. **Add platform-specific configuration model**
   ```csharp
   public class CustomPlatformConfig
   {
       public string ApiKey { get; set; }
       public string WebhookSecret { get; set; }
   }
   ```

## Database Schema

### Collection: `app_instances`

```javascript
{
  "_id": ObjectId("..."),
  "tenant_id": "tenant123",
  "platform_id": "slack",
  "name": "Support Bot",
  "agent_name": "SupportAgent",
  "activation_name": "LiveSupport",
  "workflow_id": "tenant123:SupportAgent:Supervisor Workflow:LiveSupport",
  "configuration": {
    "botToken": "encrypted_token",
    "signingSecret": "encrypted_secret",
    "appId": "A123456",
    "teamId": "T123456"
  },
  "mapping_config": {
    "participant_id_source": "userId",
    "scope_source": "channelId"
  },
  "is_active": true,
  "created_at": ISODate("2026-02-04T10:00:00Z"),
  "updated_at": ISODate("2026-02-04T10:00:00Z")
}
```

### Indexes
```javascript
db.app_instances.createIndex({ "tenant_id": 1, "platform_id": 1 });
db.app_instances.createIndex({ "workflow_id": 1 });
db.app_instances.createIndex({ "tenant_id": 1, "is_active": 1 });
```

## API Endpoints Summary

### Public Endpoints (Receive webhooks from platforms)
- `POST /api/apps/{platformId}/webhook/{appInstanceId}` - Generic webhook endpoint
- `POST /api/apps/slack/events/{appInstanceId}` - Slack Events API
- `POST /api/apps/slack/interactive/{appInstanceId}` - Slack Interactive Components
- `POST /api/apps/msteams/messaging/{appInstanceId}` - MS Teams Bot Framework

### Admin Endpoints (Manage app instances)
- `POST /api/v1/admin/tenants/{tenantId}/apps` - Create app instance
- `GET /api/v1/admin/tenants/{tenantId}/apps` - List app instances
- `GET /api/v1/admin/tenants/{tenantId}/apps/{appInstanceId}` - Get app instance
- `PUT /api/v1/admin/tenants/{tenantId}/apps/{appInstanceId}` - Update app instance
- `DELETE /api/v1/admin/tenants/{tenantId}/apps/{appInstanceId}` - Delete app instance
- `POST /api/v1/admin/tenants/{tenantId}/apps/{appInstanceId}/test` - Test app instance connection

## Implementation Status

### Phase 1: Foundation - COMPLETED
- [x] Define core interfaces and models (`AppIntegration`, `AppIntegrationMappingConfig`)
- [x] Implement `AppIntegrationRepository` with MongoDB support
- [x] Create `AppIntegrationService` for business logic
- [x] Setup public webhook endpoint routing (`/api/apps/{platformId}/events/{integrationId}`)
- [x] Create CRUD operations for app integrations

### Phase 2: First Platform Implementation - Slack - COMPLETED
- [x] Implement Slack webhook processing
- [x] Handle Slack Events API (message events, app mentions)
- [x] Handle Slack Interactive Components endpoint (stub)
- [x] Implement webhook signature verification (HMAC-SHA256)
- [x] Handle URL verification challenge
- [x] Filter bot messages to prevent loops

### Phase 3: Outbound Message Routing - COMPLETED
- [x] Implement AppMessageRouterService (background service for routing)
- [x] Origin field pattern established (`app:{platformId}:{integrationId}`)
- [x] Subscribe to message events from MessageEventPublisher
- [x] Route outbound messages to appropriate proxy (Slack implemented)
- [x] Handle delivery via incoming webhook URL or Bot API
- [x] Comprehensive logging for troubleshooting

### Phase 4: Admin API - COMPLETED
- [x] Create admin endpoints for integration management
- [x] Add configuration validation per platform
- [x] Implement test connection functionality
- [x] Add webhook URL generation
- [x] Enable/disable integration endpoints

### Phase 5: Additional Platforms - IN PROGRESS
- [x] Implement TeamsWebhookHandler
- [x] Teams Bot Framework activity processing
- [x] Teams authentication (basic JWT validation)
- [x] Teams outbound message support
- [ ] Full JWT signature verification (TODO: production hardening)
- [ ] Adaptive Cards support (TODO: enhancement)
- [ ] Implement OutlookProxy (pending)
- [ ] Add platform-specific features as needed

## Related Files & Integration Points

### Existing Files to Reference
- `/Shared/Services/MessageService.cs` - Core messaging service
- `/Shared/Repositories/ConversationRepository.cs` - Message storage
- `/Features/AdminApi/Endpoints/AdminMessagingEndpoints.cs` - Admin messaging patterns
- `/Features/UserApi/Endpoints/WebhookEndpoints.cs` - Webhook handling patterns
- `/Features/AgentApi/Endpoints/MessagingEndpoints.cs` - Agent messaging patterns

### New Files to Create
- `/Features/AppsApi/Proxies/IAppProxy.cs`
- `/Features/AppsApi/Proxies/SlackProxy.cs`
- `/Features/AppsApi/Proxies/TeamsProxy.cs`
- `/Features/AppsApi/Proxies/OutlookProxy.cs`
- `/Features/AppsApi/Services/AppProxyService.cs`
- `/Features/AppsApi/Services/AppInstanceService.cs`
- `/Features/AppsApi/Services/AppMessageRouterService.cs`
- `/Features/AppsApi/Repositories/AppInstanceRepository.cs`
- `/Features/AppsApi/Endpoints/AppProxyEndpoints.cs`
- `/Features/AppsApi/Endpoints/AdminAppEndpoints.cs`
- `/Features/AppsApi/Models/AppProxyModels.cs`
- `/Features/AppsApi/Configuration/AppsApiConfiguration.cs`

## Testing Strategy

### Unit Tests
- Test each proxy implementation independently
- Mock external API calls
- Test message transformation logic
- Test webhook verification

### Integration Tests
- Test full flow from webhook to agent and back
- Test with real platform webhooks in staging
- Test error scenarios and retries

### End-to-End Tests
- Deploy test app instances on real platforms
- Send messages from platform to agent
- Verify agent responses delivered back to platform
- Test multi-tenant isolation

## Monitoring & Observability

### Metrics to Track
- Incoming webhook requests per platform
- Message transformation success/failure rates
- Outbound message delivery success/failure rates
- Platform API call latencies
- Webhook verification failures

### Logging
- Log all webhook requests (sanitized)
- Log message transformations
- Log platform API calls and responses
- Log delivery failures with retry information

### Alerts
- Alert on high webhook verification failure rates
- Alert on platform API errors
- Alert on message delivery failures
- Alert on configuration errors

## Future Enhancements

1. **Message Templates**
   - Define platform-specific message templates
   - Support rich formatting (Slack blocks, Teams adaptive cards)
   - Template variables from agent data

2. **Interactive Features**
   - Button actions → Agent signals
   - Form submissions → Agent data
   - Modal dialogs for complex interactions

3. **Batch Operations**
   - Bulk message sending
   - Message scheduling
   - Broadcast to multiple channels

4. **Analytics**
   - Message volume by platform
   - Response time metrics
   - User engagement analytics
   - Platform-specific insights

5. **Advanced Routing**
   - Route to different agents based on message content
   - Sentiment-based routing
   - Load balancing across multiple agent instances

---

**Document Version**: 1.0  
**Last Updated**: 2026-02-04  
**Author**: Architecture Team  
**Status**: Design - Ready for Review
