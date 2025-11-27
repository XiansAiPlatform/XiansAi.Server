# Token Limiting Architecture Documentation

## Table of Contents
1. [Overview](#overview)
2. [System Architecture](#system-architecture)
3. [Component Architecture](#component-architecture)
4. [Data Model](#data-model)
5. [Sequence Diagrams](#sequence-diagrams)
6. [API Endpoints](#api-endpoints)
7. [Configuration](#configuration)
8. [Enforcement Flow](#enforcement-flow)
9. [Window Management](#window-management)

---

## Overview

The Token Limiting system enforces configurable token consumption limits per tenant and per user. It provides:

- **Per-tenant and per-user quotas** with rolling time windows
- **Real-time enforcement** at multiple entry points (REST, WebSocket, Worker)
- **Usage tracking and reporting** for observability
- **Administrative controls** via Manager UI and REST APIs

### Key Concepts

- **Tenant Default Limit**: Base quota applied to all users in a tenant unless overridden
- **User Override**: Per-user limit that replaces the tenant default for that specific user
- **Rolling Window**: Time-based window (e.g., 24 hours) that resets based on `EffectiveFrom` timestamp
- **Effective Limit Resolution**: User override > Tenant default > Global default (if configured)

---

## System Architecture

```mermaid
graph TB
    subgraph "Client Layer"
        UI[Manager UI<br/>TokenUsageManagement]
        REST[REST Client<br/>/api/user/rest/send]
        WS[WebSocket Client]
    end

    subgraph "XiansAi.Server"
        subgraph "API Layer"
            WebAPI[Web API<br/>/api/client/usage/*]
            UserAPI[User API<br/>/api/user/rest/*]
            AgentAPI[Agent API<br/>/api/agent/usage/*]
        end

        subgraph "Service Layer"
            MessageSvc[MessageService]
            TokenSvc[TokenUsageService]
            AdminSvc[TokenUsageAdminService]
        end

        subgraph "Repository Layer"
            LimitRepo[TokenUsageLimitRepository]
            WindowRepo[TokenUsageWindowRepository]
            EventRepo[TokenUsageEventRepository]
        end
    end

    subgraph "XiansAi.Lib (Worker)"
        Router[SemanticRouterHubImpl]
        ChatHandler[ChatHandler]
        TokenClient[TokenUsageClient]
    end

    subgraph "MongoDB"
        Limits[(token_usage_limits)]
        Windows[(token_usage_windows)]
        Events[(token_usage_events)]
    end

    UI --> WebAPI
    REST --> UserAPI
    WS --> UserAPI
    WebAPI --> AdminSvc
    UserAPI --> MessageSvc
    AgentAPI --> TokenSvc
    MessageSvc --> TokenSvc
    AdminSvc --> TokenSvc
    AdminSvc --> LimitRepo
    TokenSvc --> LimitRepo
    TokenSvc --> WindowRepo
    TokenSvc --> EventRepo
    Router --> TokenClient
    ChatHandler --> TokenClient
    TokenClient --> AgentAPI
    LimitRepo --> Limits
    WindowRepo --> Windows
    EventRepo --> Events
```

---

## Component Architecture

### Component Diagram

```mermaid
classDiagram
    class TokenUsageService {
        +CheckAsync(tenantId, userId) TokenUsageStatus
        +RecordAsync(record) void
        -ResolveLimitAsync() (limit, windowStart)
    }

    class TokenUsageAdminService {
        +GetStatusAsync(tenantId, userId) TokenUsageStatus
        +GetLimitsAsync(tenantId) List~TokenUsageLimit~
        +UpsertLimitAsync(request) TokenUsageLimit
        +DeleteLimitAsync(id) bool
    }

    class MessageService {
        +ProcessIncomingMessage(request) ServiceResult
        -EnsureWithinUsageAsync(participantId) void
    }

    class TokenUsageLimitRepository {
        +GetTenantLimitAsync(tenantId) TokenUsageLimit
        +GetUserLimitAsync(tenantId, userId) TokenUsageLimit
        +GetEffectiveLimitAsync(tenantId, userId) TokenUsageLimit
        +UpsertAsync(limit) TokenUsageLimit
    }

    class TokenUsageWindowRepository {
        +GetWindowAsync(tenantId, userId, windowStart, windowSeconds) TokenUsageWindow
        +IncrementWindowAsync(tenantId, userId, windowStart, windowSeconds, tokens) TokenUsageWindow
    }

    class TokenUsageEventRepository {
        +InsertAsync(event) void
        +GetEventsAsync(tenantId, userId, since) List~TokenUsageEvent~
    }

    class TokenUsageClient {
        +EnsureWithinLimitAsync(workflowId, participantId) void
        +ReportAsync(report) void
        +EstimateTokens(content) long
    }

    class SemanticRouterHubImpl {
        +RouteAsync(messageThread, systemPrompt, options) string
        -EstimatePromptTokens(history, message) long
    }

    TokenUsageService --> TokenUsageLimitRepository
    TokenUsageService --> TokenUsageWindowRepository
    TokenUsageService --> TokenUsageEventRepository
    TokenUsageAdminService --> TokenUsageService
    TokenUsageAdminService --> TokenUsageLimitRepository
    MessageService --> TokenUsageService
    SemanticRouterHubImpl --> TokenUsageClient
    TokenUsageClient ..> TokenUsageService : via HTTP
```

---

## Data Model

### Entity Relationship Diagram

```mermaid
erDiagram
    TokenUsageLimit ||--o{ TokenUsageWindow : "defines"
    TokenUsageWindow ||--o{ TokenUsageEvent : "tracks"
    
    TokenUsageLimit {
        string id PK
        string tenant_id
        string user_id "nullable"
        long max_tokens
        int window_seconds
        datetime effective_from
        bool enabled
        datetime created_at
        datetime updated_at
        string updated_by
    }
    
    TokenUsageWindow {
        string id PK
        string tenant_id
        string user_id
        datetime window_start
        int window_seconds
        long tokens_used
        datetime updated_at
    }
    
    TokenUsageEvent {
        string id PK
        string tenant_id
        string user_id
        string model
        long prompt_tokens
        long completion_tokens
        long total_tokens
        string workflow_id
        string request_id
        string source
        dict metadata
        datetime created_at
    }
```

### Data Model Details

#### TokenUsageLimit
- **Purpose**: Defines quota limits (max tokens, window duration) per tenant or user
- **Key Fields**:
  - `tenantId`: Required tenant identifier
  - `userId`: `null` for tenant-level limits, user ID for overrides
  - `maxTokens`: Maximum tokens allowed in the window
  - `windowSeconds`: Rolling window duration (60 to 2,592,000 seconds)
  - `effectiveFrom`: Timestamp from which the window calculation starts
  - `enabled`: Master switch for this limit
- **Indexes**: `{ tenantId: 1, userId: 1 }`

#### TokenUsageWindow
- **Purpose**: Tracks current usage within a specific time window
- **Key Fields**:
  - `tenantId`, `userId`: Composite key with window boundaries
  - `windowStart`: Calculated start of current window (aligned to `effectiveFrom`)
  - `windowSeconds`: Window duration (must match limit's `windowSeconds`)
  - `tokensUsed`: Accumulated token count (atomic increments)
- **Indexes**: `{ tenantId: 1, userId: 1, windowStart: -1 }`

#### TokenUsageEvent
- **Purpose**: Audit log of individual LLM invocations
- **Key Fields**:
  - `promptTokens`, `completionTokens`, `totalTokens`: Token counts
  - `source`: Origin of the call (e.g., "SemanticRouter.Route", "MarkdownService")
  - `workflowId`, `requestId`: Correlation identifiers
  - `metadata`: Additional context (workflowType, participantId, etc.)

---

## Sequence Diagrams

### 1. Message Processing with Token Check

```mermaid
sequenceDiagram
    participant Client
    participant UserAPI as User API Endpoint
    participant MessageSvc as MessageService
    participant TokenSvc as TokenUsageService
    participant LimitRepo as LimitRepository
    participant WindowRepo as WindowRepository
    participant MongoDB

    Client->>UserAPI: POST /api/user/rest/send
    UserAPI->>MessageSvc: ProcessIncomingMessage(request)
    
    MessageSvc->>MessageSvc: EnsureWithinUsageAsync(participantId)
    MessageSvc->>TokenSvc: CheckAsync(tenantId, userId)
    
    TokenSvc->>LimitRepo: GetEffectiveLimitAsync(tenantId, userId)
    LimitRepo->>MongoDB: Query token_usage_limits
    MongoDB-->>LimitRepo: Limit (or null)
    LimitRepo-->>TokenSvc: effectiveLimit
    
    alt Limit found
        TokenSvc->>TokenSvc: Calculate windowStart from effectiveFrom
        TokenSvc->>WindowRepo: GetWindowAsync(tenantId, userId, windowStart, windowSeconds)
        WindowRepo->>MongoDB: Query token_usage_windows
        MongoDB-->>WindowRepo: Window (or null)
        WindowRepo-->>TokenSvc: usageWindow
        
        TokenSvc->>TokenSvc: Calculate tokensUsed, remaining, isExceeded
        TokenSvc-->>MessageSvc: TokenUsageStatus
        
        alt Limit exceeded
            MessageSvc-->>UserAPI: ServiceResult.Forbidden("TOKEN_USAGE_EXCEEDED")
            UserAPI-->>Client: HTTP 403 { error: "TOKEN_USAGE_EXCEEDED" }
        else Within limit
            MessageSvc->>MessageSvc: SaveMessage()
            MessageSvc->>MessageSvc: SignalWorkflowAsync()
            MessageSvc-->>UserAPI: ServiceResult.Success(threadId)
            UserAPI-->>Client: HTTP 200 { threadId }
        end
    else No limit (disabled)
        TokenSvc-->>MessageSvc: DisabledStatus()
        MessageSvc->>MessageSvc: SaveMessage()
        MessageSvc-->>UserAPI: ServiceResult.Success(threadId)
        UserAPI-->>Client: HTTP 200 { threadId }
    end
```

### 2. Token Usage Recording (Worker Flow)

```mermaid
sequenceDiagram
    participant Worker as Temporal Worker
    participant Router as SemanticRouterHubImpl
    participant TokenClient as TokenUsageClient
    participant AgentAPI as Agent API
    participant TokenSvc as TokenUsageService
    participant WindowRepo as WindowRepository
    participant EventRepo as EventRepository
    participant MongoDB

    Worker->>Router: RouteAsync(messageThread, systemPrompt)
    Router->>TokenClient: EnsureWithinLimitAsync(workflowId, participantId)
    
    TokenClient->>AgentAPI: GET /api/agent/usage/status?userId=...
    AgentAPI->>TokenSvc: CheckAsync(tenantId, userId)
    TokenSvc-->>AgentAPI: TokenUsageStatus
    AgentAPI-->>TokenClient: { isExceeded: false }
    
    alt Limit not exceeded
        TokenClient-->>Router: Continue
        Router->>Router: Build chat history
        Router->>Router: Invoke agent (LLM call)
        Router->>Router: Estimate tokens (prompt + completion)
        
        Router->>TokenClient: ReportAsync(TokenUsageReport)
        TokenClient->>AgentAPI: POST /api/agent/usage/report
        AgentAPI->>TokenSvc: RecordAsync(record)
        
        TokenSvc->>TokenSvc: ResolveLimitAsync(tenantId, userId)
        TokenSvc->>TokenSvc: Calculate windowStart
        
        TokenSvc->>WindowRepo: IncrementWindowAsync(tenantId, userId, windowStart, windowSeconds, totalTokens)
        WindowRepo->>MongoDB: Upsert with $inc on tokens_used
        MongoDB-->>WindowRepo: Updated window
        
        alt RecordUsageEvents enabled
            TokenSvc->>EventRepo: InsertAsync(usageEvent)
            EventRepo->>MongoDB: Insert token_usage_events
        end
        
        TokenSvc-->>AgentAPI: Success
        AgentAPI-->>TokenClient: HTTP 202 Accepted
        TokenClient-->>Router: Success
        Router-->>Worker: Response text
    else Limit exceeded
        TokenClient-->>Router: TokenLimitExceededException
        Router-->>Worker: Exception (blocked)
    end
```

### 3. Limit Management (Admin UI Flow)

```mermaid
sequenceDiagram
    participant Admin as Admin User
    participant UI as Manager UI
    participant WebAPI as Web API
    participant AdminSvc as TokenUsageAdminService
    participant LimitRepo as LimitRepository
    participant MongoDB

    Admin->>UI: Edit tenant limit (maxTokens: 250, windowSeconds: 600)
    UI->>WebAPI: POST /api/client/usage/limits
    
    WebAPI->>AdminSvc: UpsertLimitAsync(request)
    
    AdminSvc->>LimitRepo: GetTenantLimitAsync(tenantId)
    LimitRepo->>MongoDB: Query existing limit
    MongoDB-->>LimitRepo: existingLimit (or null)
    LimitRepo-->>AdminSvc: existingLimit
    
    alt Existing limit found
        AdminSvc->>AdminSvc: Check if maxTokens or windowSeconds changed
        alt Window-affecting fields changed
            AdminSvc->>AdminSvc: Set effectiveFrom = DateTime.UtcNow (new window)
        else Only enabled or other fields changed
            AdminSvc->>AdminSvc: Preserve existing effectiveFrom (keep window)
        end
    else New limit
        AdminSvc->>AdminSvc: Set effectiveFrom = DateTime.UtcNow
    end
    
    AdminSvc->>LimitRepo: UpsertAsync(limit)
    LimitRepo->>MongoDB: Upsert token_usage_limits
    MongoDB-->>LimitRepo: Saved limit
    LimitRepo-->>AdminSvc: TokenUsageLimit
    AdminSvc-->>WebAPI: ServiceResult.Success(limit)
    WebAPI-->>UI: HTTP 200 { limit }
    UI->>Admin: Show success notification
```

### 4. Effective Limit Resolution

```mermaid
sequenceDiagram
    participant TokenSvc as TokenUsageService
    participant LimitRepo as LimitRepository
    participant MongoDB

    TokenSvc->>LimitRepo: GetEffectiveLimitAsync(tenantId, userId)
    
    alt userId provided
        LimitRepo->>MongoDB: Query user limit (tenantId, userId)
        MongoDB-->>LimitRepo: userLimit
        
        alt userLimit exists and enabled
            LimitRepo-->>TokenSvc: userLimit
        else userLimit missing or disabled
            LimitRepo->>MongoDB: Query tenant limit (tenantId, userId=null)
            MongoDB-->>LimitRepo: tenantLimit
            
            alt tenantLimit exists and enabled
                LimitRepo-->>TokenSvc: tenantLimit
            else tenantLimit missing or disabled
                LimitRepo-->>TokenSvc: null
            end
        end
    else userId null
        LimitRepo->>MongoDB: Query tenant limit (tenantId, userId=null)
        MongoDB-->>LimitRepo: tenantLimit
        
        alt tenantLimit exists and enabled
            LimitRepo-->>TokenSvc: tenantLimit
        else tenantLimit missing or disabled
            LimitRepo-->>TokenSvc: null
        end
    end
    
    alt No limit found
        TokenSvc->>TokenSvc: Check TokenUsageOptions.DefaultTenantLimit
        alt DefaultTenantLimit > 0
            TokenSvc->>TokenSvc: Create synthetic limit from options
            TokenSvc-->>TokenSvc: Synthetic limit (not persisted)
        else DefaultTenantLimit <= 0
            TokenSvc-->>TokenSvc: null (feature disabled)
        end
    end
```

---

## API Endpoints

### Web API (Manager UI)

| Endpoint | Method | Description | Auth |
|----------|--------|-------------|------|
| `/api/client/usage/status` | GET | Get current usage status for tenant/user | Tenant Admin |
| `/api/client/usage/limits` | GET | List all limits for tenant | Tenant Admin |
| `/api/client/usage/limits` | POST | Create/update limit | Tenant Admin |
| `/api/client/usage/limits/{id}` | DELETE | Delete limit | Tenant Admin |

### Agent API (Worker)

| Endpoint | Method | Description | Auth |
|----------|--------|-------------|------|
| `/api/agent/usage/status` | GET | Get usage status for certificate tenant/user | Certificate |
| `/api/agent/usage/report` | POST | Report token usage from LLM call | Certificate |

### User API (Client)

| Endpoint | Method | Description | Auth |
|----------|--------|-------------|------|
| `/api/user/rest/send` | POST | Send message (enforced) | API Key / JWT |

---

## Configuration

### TokenUsageOptions

```csharp
{
  "TokenUsage": {
    "Enabled": true,                    // Master feature flag
    "DefaultTenantLimit": 200000,       // Default quota when no limit configured
    "WindowSeconds": 86400,             // Default window (24 hours)
    "WarningPercentage": 0.8,           // Warn at 80% usage
    "RecordUsageEvents": true,          // Enable audit logging
    "MaxUsageHistoryDays": 30,          // Retention period
    "AllowUserOverrides": true          // Allow per-user limits
  }
}
```

### Environment Variables

- `TokenUsage:Enabled`: Feature toggle (default: `true`)
- `TokenUsage:DefaultTenantLimit`: Fallback quota (default: `200000`)
- `TokenUsage:WindowSeconds`: Default window duration (default: `86400`)

---

## Enforcement Flow

### Enforcement Points

1. **MessageService.ProcessIncomingMessage**
   - Checks limit before saving message
   - Throws `TokenLimitExceededException` if exceeded
   - Returns `ServiceResult.Forbidden("TOKEN_USAGE_EXCEEDED")`

2. **SemanticRouterHubImpl.RouteAsync** (Worker)
   - Checks limit before LLM invocation
   - Throws `TokenLimitExceededException` to abort workflow
   - Reports usage after successful LLM call

3. **SemanticRouterHubImpl.CompletionAsync** (Worker)
   - Checks limit before completion
   - Reports usage after completion

### Identity Resolution

- **Server-side**: Uses `TenantContext.LoggedInUser` (authenticated user)
- **Worker-side**: Uses `AgentContext.UserId` (from certificate)
- **ParticipantId**: Treated as display alias only, not used for quota enforcement

---

## Window Management

### Window Calculation

The rolling window is calculated based on:

1. **EffectiveFrom**: Timestamp when the limit was created or last modified (if window-affecting fields changed)
2. **WindowSeconds**: Duration of the window
3. **Current Time**: Used to determine which window period we're in

```csharp
// Pseudo-code
elapsedSeconds = (now - effectiveFrom).TotalSeconds
completedWindows = floor(elapsedSeconds / windowSeconds)
windowStart = effectiveFrom + (completedWindows * windowSeconds)
windowEndsAt = windowStart + windowSeconds
```

### Window Reset Behavior

- **New Limit**: `EffectiveFrom = DateTime.UtcNow` → Fresh window starts now
- **Limit Updated (maxTokens/windowSeconds changed)**: `EffectiveFrom = DateTime.UtcNow` → New window starts
- **Limit Updated (only enabled toggled)**: `EffectiveFrom` preserved → Existing window continues
- **Automatic Reset**: When `windowEndsAt < now`, next check calculates a new window

### Window Persistence

- Windows are keyed by `(tenantId, userId, windowStart, windowSeconds)`
- Old windows remain in database for historical analysis
- Only the current window (matching current `windowStart`) is used for enforcement

---

## Error Handling

### TokenLimitExceededException

- **Server**: Caught in `MessageService`, returns HTTP 403
- **Worker**: Propagated to workflow, aborts LLM call
- **Frontend**: Special-cased in `api-client.js` to show user-friendly toast instead of redirect

### Error Response Format

```json
{
  "error": "TOKEN_USAGE_EXCEEDED"
}
```

Frontend recognizes this and displays:
- Title: "Token Limit Exceeded"
- Message: "Token usage limit exceeded. You have reached your token quota for this period."
- Actions: ["Wait for the usage window to reset", "Contact your administrator", ...]

---

## Monitoring & Logging

### Log Events

1. **Pre-check**: `Token usage pre-check: tenant={TenantId}, user={UserId}, participant={ParticipantId}`
2. **Status**: `Token usage status: ... used={TokensUsed}, remaining={TokensRemaining}, max={MaxTokens}`
3. **Recording**: `Recording token usage: tenant={TenantId}, user={UserId}, totalTokens={TotalTokens}`
4. **Warning**: `Token usage warning for tenant {TenantId}, user {UserId}. Used={Used}/{Limit}` (at 80% threshold)
5. **Exceeded**: `Token usage exceeded for tenant {TenantId}, user {UserId}. Limit={Limit}, Used={Used}`

### Metrics (Future)

- `token.usage.total`: Total tokens consumed
- `token.usage.remaining`: Remaining quota
- `token.limit.exceeded`: Count of limit violations
- `token.window.reset`: Window reset events

---

## Testing Strategy

### Unit Tests

- `TokenUsageServiceTests`: Limit resolution, window calculation, exceeded conditions
- `MessageServiceTests`: Enforcement integration, error handling
- `TokenUsageAdminServiceTests`: Limit CRUD operations, window preservation

### Integration Tests

- `UsageEndpointsTests`: API endpoint validation, authorization
- `TokenUsageEndpointsTests`: Agent API status/report flows
- `MessageServiceIntegrationTests`: End-to-end message processing with limits

---

## Related Documentation

- [TOKEN_LIMITING_IMPLEMENTATION_PLAN.md](./TOKEN_LIMITING_IMPLEMENTATION_PLAN.md)
- [TOKEN_LIMITING_PHASES.md](./TOKEN_LIMITING_PHASES.md)
- [webapi/TOKEN_USAGE.md](./webapi/TOKEN_USAGE.md)
- [user-api/rest-api.md](./user-api/rest-api.md)

---

## Revision History

- **2024-11-26**: Initial architecture documentation
- Includes: Component diagrams, sequence diagrams, data models, enforcement flows

