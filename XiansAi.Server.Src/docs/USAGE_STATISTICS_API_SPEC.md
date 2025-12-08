# Usage Statistics API Specification

## Overview
This document defines the API requirements for the Usage Statistics Dashboard that allows users to view token usage and message usage metrics with filtering and breakdown capabilities.

---

## Table of Contents
1. [API Endpoints](#api-endpoints)
2. [Security & Authorization](#security--authorization)
3. [Data Models](#data-models)
4. [Aggregation Logic](#aggregation-logic)
5. [Implementation Plan](#implementation-plan)

---

## API Endpoints

### 1. Get Token Usage Statistics

**Endpoint:** `GET /api/client/usage/statistics/tokens`

**Description:** Retrieves aggregated token usage statistics with filtering and grouping options.

**Query Parameters:**
```
- tenantId (string, optional): Tenant ID (sys-admin only, defaults to current tenant)
- userId (string, optional): Filter by specific user OR "all" for all users (hidden for non-admin users)
- startDate (DateTime, required): Start of date range (ISO 8601 format)
- endDate (DateTime, required): End of date range (ISO 8601 format)
- groupBy (enum: "day" | "week" | "month", optional, default: "day"): Time grouping for chart
```

**Authorization:** 
- **Admin (SysAdmin/TenantAdmin):** Can view all users in tenant, can specify userId or "all"
- **Regular User:** Auto-filtered to their own userId, cannot specify different userId

**Note:** Model information is included in the response data for informational purposes but cannot be filtered.

**Response:**
```json
{
  "tenantId": "tenant123",
  "userId": null,  // null if showing all users, specific userId if filtered
  "startDate": "2025-12-01T00:00:00Z",
  "endDate": "2025-12-08T23:59:59Z",
  "totalTokens": 1500000,
  "totalPromptTokens": 900000,
  "totalCompletionTokens": 600000,
  "totalRequests": 450,
  "timeSeriesData": [
    {
      "date": "2025-12-01",
      "totalTokens": 200000,
      "promptTokens": 120000,
      "completionTokens": 80000,
      "requestCount": 60
    },
    {
      "date": "2025-12-02",
      "totalTokens": 220000,
      "promptTokens": 132000,
      "completionTokens": 88000,
      "requestCount": 65
    }
    // ... more dates
  ],
  "userBreakdown": [
    {
      "userId": "user789",
      "userName": "Jane Smith",
      "totalTokens": 1000000,
      "promptTokens": 600000,
      "completionTokens": 400000,
      "requestCount": 300
    },
    {
      "userId": "user456",
      "userName": "John Doe",
      "totalTokens": 500000,
      "promptTokens": 300000,
      "completionTokens": 200000,
      "requestCount": 150
    }
  ]
}
```

---

### 2. Get Message Usage Statistics

**Endpoint:** `GET /api/client/usage/statistics/messages`

**Description:** Retrieves aggregated message count statistics with filtering and grouping options.

**Query Parameters:**
```
- tenantId (string, optional): Tenant ID (sys-admin only, defaults to current tenant)
- userId (string, optional): Filter by specific user OR "all" for all users (hidden for non-admin users)
- startDate (DateTime, required): Start of date range (ISO 8601 format)
- endDate (DateTime, required): End of date range (ISO 8601 format)
- groupBy (enum: "day" | "week" | "month", optional, default: "day"): Time grouping for chart
```

**Authorization:** 
- **Admin (SysAdmin/TenantAdmin):** Can view all users in tenant, can specify userId or "all"
- **Regular User:** Auto-filtered to their own userId, cannot specify different userId

**Note:** Model information is included in the response data for informational purposes but cannot be filtered.

**Response:**
```json
{
  "tenantId": "tenant123",
  "userId": null,  // null if showing all users, specific userId if filtered
  "startDate": "2025-12-01T00:00:00Z",
  "endDate": "2025-12-08T23:59:59Z",
  "totalMessages": 450,
  "totalRequests": 450,
  "timeSeriesData": [
    {
      "date": "2025-12-01",
      "messageCount": 60,
      "requestCount": 60
    },
    {
      "date": "2025-12-02",
      "messageCount": 65,
      "requestCount": 65
    }
    // ... more dates
  ],
  "userBreakdown": [
    {
      "userId": "user789",
      "userName": "Jane Smith",
      "messageCount": 300,
      "requestCount": 300
    },
    {
      "userId": "user456",
      "userName": "John Doe",
      "messageCount": 150,
      "requestCount": 150
    }
  ]
}
```

---

### 3. Get Users with Usage

**Endpoint:** `GET /api/client/usage/statistics/users`

**Description:** Returns list of users with token/message usage in the tenant (admin only).

**Query Parameters:**
```
- tenantId (string, optional): Tenant ID (sys-admin only)
- startDate (DateTime, optional): Filter users with usage after this date
- endDate (DateTime, optional): Filter users with usage before this date
```

**Authorization:** `RequireTenantAdmin`

**Response:**
```json
{
  "users": [
    {
      "userId": "user789",
      "userName": "Jane Smith",
      "email": "jane@example.com"
    },
    {
      "userId": "user456",
      "userName": "John Doe",
      "email": "john@example.com"
    }
  ]
}
```

**Note:** This endpoint returns a simple list for the user filter dropdown. Actual usage data comes from the token/message statistics endpoints.

---

---

## Security & Authorization

### Role-Based Access Control

#### 1. **SysAdmin**
- Can view usage across all tenants
- Can specify any `tenantId` parameter
- Can view breakdown by any user

#### 2. **TenantAdmin**
- Can view usage within their tenant only
- `tenantId` parameter defaults to their tenant (cannot override)
- Can view breakdown by any user in their tenant

#### 3. **TenantUser**
- Can view only their own usage
- `tenantId` parameter defaults to their tenant (cannot override)
- `userId` parameter is auto-set to their user ID (cannot override)
- User filter UI is hidden

### Authorization Policies

```csharp
// Apply to all statistics endpoints
.RequiresValidTenant()
.RequireAuthorization("RequireTenantAuth")

// For endpoints that expose user-level data
private static void ValidateUserAccess(ITenantContext context, string? requestedUserId)
{
    // SysAdmin can access any user
    if (context.UserRoles.Contains(SystemRoles.SysAdmin))
        return;
    
    // TenantAdmin can access users in their tenant
    if (context.UserRoles.Contains(SystemRoles.TenantAdmin))
        return;
    
    // Regular users can only access their own data
    if (!string.IsNullOrEmpty(requestedUserId) && 
        requestedUserId != context.LoggedInUser)
    {
        throw new UnauthorizedAccessException("You can only view your own usage data");
    }
}
```

### Rate Limiting

Apply standard rate limiting to all statistics endpoints:
```csharp
.WithGlobalRateLimit()
```

---

## Data Models

### 1. TokenUsageStatisticsResponse
```csharp
public record TokenUsageStatisticsResponse
{
    public required string TenantId { get; init; }
    public string? UserId { get; init; }  // null = all users, value = specific user
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public long TotalTokens { get; init; }
    public long TotalPromptTokens { get; init; }
    public long TotalCompletionTokens { get; init; }
    public int TotalRequests { get; init; }
    public List<TimeSeriesDataPoint> TimeSeriesData { get; init; }
    public List<UserTokenBreakdown> UserBreakdown { get; init; }
}
```

### 2. MessageUsageStatisticsResponse
```csharp
public record MessageUsageStatisticsResponse
{
    public required string TenantId { get; init; }
    public string? UserId { get; init; }  // null = all users, value = specific user
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public long TotalMessages { get; init; }
    public int TotalRequests { get; init; }
    public List<TimeSeriesDataPoint> TimeSeriesData { get; init; }
    public List<UserMessageBreakdown> UserBreakdown { get; init; }
}
```

### 3. TimeSeriesDataPoint
```csharp
public record TimeSeriesDataPoint
{
    public DateTime Date { get; init; }
    public long TotalTokens { get; init; }      // For token stats
    public long PromptTokens { get; init; }     // For token stats
    public long CompletionTokens { get; init; } // For token stats
    public long MessageCount { get; init; }     // For message stats
    public int RequestCount { get; init; }
}
```

### 4. UserTokenBreakdown
```csharp
public record UserTokenBreakdown
{
    public required string UserId { get; init; }
    public string? UserName { get; init; }
    public long TotalTokens { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public int RequestCount { get; init; }
}
```

### 5. UserMessageBreakdown
```csharp
public record UserMessageBreakdown
{
    public required string UserId { get; init; }
    public string? UserName { get; init; }
    public long MessageCount { get; init; }
    public int RequestCount { get; init; }
}
```

### 6. UserListItem
```csharp
public record UserListItem
{
    public required string UserId { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
}
```

---

## Aggregation Logic

### MongoDB Aggregation Pipeline for Token Statistics

#### Time Series Aggregation (Group by Day):
```csharp
var timeSeriesPipeline = new[]
{
    // Match stage - filter by tenant, user, date range
    new BsonDocument("$match", new BsonDocument
    {
        { "tenant_id", tenantId },
        { "user_id", string.IsNullOrEmpty(userId) 
            ? new BsonDocument("$exists", true) 
            : userId },
        { "created_at", new BsonDocument
            {
                { "$gte", startDate },
                { "$lte", endDate }
            }
        }
    }),
    
    // Group by date
    new BsonDocument("$group", new BsonDocument
    {
        { "_id", new BsonDocument("$dateToString", new BsonDocument
            {
                { "format", "%Y-%m-%d" },
                { "date", "$created_at" }
            })
        },
        { "totalTokens", new BsonDocument("$sum", "$total_tokens") },
        { "promptTokens", new BsonDocument("$sum", "$prompt_tokens") },
        { "completionTokens", new BsonDocument("$sum", "$completion_tokens") },
        { "messageCount", new BsonDocument("$sum", "$message_count") },
        { "requestCount", new BsonDocument("$sum", 1) }
    }),
    
    // Sort by date
    new BsonDocument("$sort", new BsonDocument("_id", 1))
};
```

#### User Breakdown Aggregation:
```csharp
var userBreakdownPipeline = new[]
{
    // Match stage - filter by tenant, date range
    new BsonDocument("$match", new BsonDocument
    {
        { "tenant_id", tenantId },
        { "created_at", new BsonDocument
            {
                { "$gte", startDate },
                { "$lte", endDate }
            }
        }
    }),
    
    // Group by user
    new BsonDocument("$group", new BsonDocument
    {
        { "_id", "$user_id" },
        { "totalTokens", new BsonDocument("$sum", "$total_tokens") },
        { "promptTokens", new BsonDocument("$sum", "$prompt_tokens") },
        { "completionTokens", new BsonDocument("$sum", "$completion_tokens") },
        { "messageCount", new BsonDocument("$sum", "$message_count") },
        { "requestCount", new BsonDocument("$sum", 1) }
    }),
    
    // Sort by total tokens descending
    new BsonDocument("$sort", new BsonDocument("totalTokens", -1)),
    
    // Limit to top 100 users
    new BsonDocument("$limit", 100)
};
```

### Performance Considerations

1. **Indexes Required:**
   ```javascript
   // Compound index for date range queries
   db.token_usage_events.createIndex({ 
       "tenant_id": 1, 
       "created_at": -1 
   })
   
   // Compound index for user-specific queries
   db.token_usage_events.createIndex({ 
       "tenant_id": 1, 
       "user_id": 1, 
       "created_at": -1 
   })
   
   ```

2. **Query Optimization:**
   - Limit date range to maximum 90 days
   - Use pagination for large result sets
   - Cache aggregation results for frequently accessed time periods
   - Consider pre-aggregated daily/hourly summaries for very high volume

3. **Response Time Targets:**
   - Simple queries (single user, < 30 days): < 500ms
   - Complex aggregations (all users, grouped): < 2s
   - Export operations: < 10s

---

## Implementation Plan

### Phase 1: Repository Layer
**File:** `Shared/Repositories/TokenUsageRepository.cs`

Add methods to `ITokenUsageEventRepository`:
```csharp
Task<TokenUsageStatisticsResponse> GetTokenStatisticsAsync(
    string tenantId, 
    string? userId,  // null or "all" = all users, specific userId = filtered
    DateTime startDate, 
    DateTime endDate, 
    string groupBy = "day",  // "day" | "week" | "month"
    CancellationToken cancellationToken = default);

Task<MessageUsageStatisticsResponse> GetMessageStatisticsAsync(
    string tenantId, 
    string? userId,  // null or "all" = all users, specific userId = filtered
    DateTime startDate, 
    DateTime endDate, 
    string groupBy = "day",  // "day" | "week" | "month"
    CancellationToken cancellationToken = default);

Task<List<UserListItem>> GetUsersAsync(
    string tenantId, 
    CancellationToken cancellationToken = default);
```

### Phase 2: Service Layer
**File:** `Shared/Services/UsageStatisticsService.cs` (NEW)

```csharp
public interface IUsageStatisticsService
{
    Task<TokenUsageStatisticsResponse> GetTokenStatisticsAsync(
        UsageStatisticsRequest request, 
        CancellationToken cancellationToken = default);
    
    Task<MessageUsageStatisticsResponse> GetMessageStatisticsAsync(
        UsageStatisticsRequest request, 
        CancellationToken cancellationToken = default);
}
```

### Phase 3: API Endpoints
**File:** `Features/WebApi/Endpoints/UsageStatisticsEndpoints.cs` (NEW)

Implement all 4 endpoints defined above.

### Phase 4: Testing
- Unit tests for repository aggregation logic
- Integration tests for API endpoints
- Security tests for authorization rules
- Performance tests for large datasets

---

## Error Handling

### Validation Errors (400 Bad Request)
- Missing required parameters (startDate, endDate)
- Invalid date range (end before start)
- Date range too large (> 90 days)
- Invalid groupBy value (must be "day", "week", or "month")
- Invalid userId format

### Authorization Errors (403 Forbidden)
- User attempting to view other users' data (non-admin)
- User attempting to access different tenant data

### Server Errors (500 Internal Server Error)
- Database aggregation failures
- Timeout on large queries

### Error Response Format
```json
{
  "error": "Invalid date range",
  "message": "End date must be after start date",
  "code": "INVALID_DATE_RANGE"
}
```

---

## API Versioning

All endpoints should be versioned:
```
/api/v1/client/usage/statistics/*
```

This allows for future changes without breaking existing clients.

---

## Documentation

- Add Swagger/OpenAPI documentation for all endpoints
- Include example requests and responses
- Document authorization requirements clearly
- Provide sample queries for common use cases

