# Usage Analytics Dashboard - Implementation Plan

## Table of Contents
1. [Overview](#overview)
2. [Current State Analysis](#current-state-analysis)
3. [Proposed Features](#proposed-features)
4. [Data Model Changes](#data-model-changes)
5. [Backend API Requirements](#backend-api-requirements)
6. [Frontend Components](#frontend-components)
7. [Implementation Phases](#implementation-phases)
8. [Performance Considerations](#performance-considerations)
9. [Future Enhancements](#future-enhancements)

---

## Overview

### Purpose
Create a comprehensive usage analytics dashboard that enables tenant administrators to:
- View historical token usage patterns over customizable date ranges
- Analyze usage at tenant, user, and agent levels
- Support multiple metric types (Tokens now, Messages in future)
- Visualize trends through charts and breakdowns

### Business Value
- **Usage Insights**: Identify high-usage users, agents, and workflows
- **Capacity Planning**: Forecast quota needs based on historical trends
- **Audit Trail**: Maintain detailed records of all LLM operations
- **Optimization**: Analyze patterns to improve efficiency

---

## Current State Analysis

### What's Being Collected

#### 1. `token_usage_events` Collection
**Currently Logged Fields:**
```javascript
{
  "tenant_id": "string",
  "user_id": "string",
  "model": "string",              // e.g., "gpt-4o"
  "prompt_tokens": 150,
  "completion_tokens": 75,
  "total_tokens": 225,
  "workflow_id": "string",
  "request_id": "string",
  "source": "string",             // e.g., "SemanticRouter.Route"
  "agent_name": "string",         // e.g., "agent-customer-support" (NEW)
  "metadata": {
    "workflowType": "string",
    "participantId": "string"
  },
  "created_at": "2025-11-28T10:00:00Z"
}
```

**Controlled By:** `TokenUsageOptions.RecordUsageEvents` (default: `true`)

**Current Behavior:**
- ✅ One event record per LLM invocation
- ✅ Full token breakdown (prompt, completion, total)
- ✅ Captures model, source, and workflow context
- ✅ Timestamp for historical analysis

#### 2. `token_usage_windows` Collection
**Purpose:** Real-time aggregate tracking for quota enforcement

```javascript
{
  "tenant_id": "string",
  "user_id": "string",
  "window_start": "2025-11-28T00:00:00Z",
  "window_seconds": 86400,
  "tokens_used": 125000,          // Rolling total only
  "updated_at": "2025-11-28T10:00:00Z"
}
```

**Limitation:** Only stores total, no breakdown by model/source/cost

#### 3. `token_usage_limits` Collection
**Purpose:** Quota configuration

```javascript
{
  "tenant_id": "string",
  "user_id": "string?",           // null = tenant default
  "max_tokens": 200000,
  "window_seconds": 86400,
  "effective_from": "2025-11-01T00:00:00Z",
  "enabled": true
}
```

### What's Currently Displayed in UI

#### System Admin View (`/manager/admin` → "Usage Limits")
**Component:** `TokenUsageManagement.jsx`

**Features:**
- ✅ Tenant selector
- ✅ Current usage stats card (aggregate only)
- ✅ Edit tenant default limits
- ✅ Manage per-user overrides

**Displayed Metrics:**
- Tokens Used / Max Tokens
- Tokens Remaining
- Window Reset Time
- Progress bar (% used)

#### Tenant Admin View (`/manager/settings` → "Usage")
**Component:** `TenantUsage.jsx`

**Features:**
- ✅ Usage stats card for own tenant
- ✅ Edit tenant limit
- ✅ View override count (read-only)

### Critical Gap Identified

❌ **No visibility into detailed event history**
- Cannot view individual LLM calls
- Cannot analyze usage trends over time
- Cannot break down by model, user, agent, or source
- Cannot export data for reporting

**The data exists (`token_usage_events`) but is not exposed in the UI.**

---

## Proposed Features

### Dashboard Requirements

#### 1. Filters & Selection
- **Tenant Selector**: Admin chooses which tenant to analyze
- **User Selector** (optional): Narrow down to specific user, null = all users
- **Date Range Picker**: Custom start/end dates (defaults to last 7 days)
- **Metric Type Toggle**: Radio buttons for "Tokens" / "Messages" (future)
- **Aggregation Granularity**: Auto-select Hour/Day/Week based on date range

#### 2. Summary Cards (Top Row)

```
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│ Total Tokens  │ │ Total Events  │ │ Avg/Event     │
│   1,250,000   │ │     543       │ │   2,302       │
└───────────────┘ └───────────────┘ └───────────────┘
```

#### 3. Time Series Chart

**Chart Type:** Line or Area chart  
**X-Axis:** Time (hourly/daily/weekly buckets)  
**Y-Axis:** Token count  
**Library:** Recharts (already used in project)

**Features:**
- Hover tooltips showing exact values
- Zoom/pan for detailed inspection

#### 4. Breakdown Visualizations

**By Model:**
```
gpt-4o        ████████████████████░░ 67% (800K tokens)
gpt-4o-mini   ██████████░░░░░░░░░░░░ 33% (450K tokens)
```

**By Agent** (Top 10):
```
agent-customer-support  ██████████████████░░ 52% (650K tokens)
agent-data-analyst      ████████████░░░░░░░░ 31% (387K tokens)
agent-content-writer    ██████░░░░░░░░░░░░░░ 17% (213K tokens)
...
```

**By User** (Top 10):
```
user-123    ██████████████░░░░░░░░ 45% (562K tokens)
user-456    ████████░░░░░░░░░░░░░░ 28% (350K tokens)
...
```

#### 5. Data Table (Bottom Section)

**Columns:**
- Timestamp
- User ID
- Agent Name
- Model
- Source
- Prompt Tokens
- Completion Tokens
- Total Tokens

**Features:**
- Sortable columns
- Pagination (50/100/200 per page)
- Export to CSV

#### 6. Export Functionality

**Formats:**
- CSV (raw events)
- JSON (aggregated summary)

**Includes:**
- Filters applied
- Date range
- All visible columns

---

## Data Model Changes

### 1. Update `TokenUsageEvent` Model

**File:** `Shared/Data/Models/Usage/TokenUsageModels.cs`

**New Fields to Add:**

```csharp
[BsonIgnoreExtraElements]
public class TokenUsageEvent
{
    // ========== EXISTING FIELDS ==========
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    
    [BsonElement("tenant_id")]
    [Required]
    public required string TenantId { get; set; }
    
    [BsonElement("user_id")]
    public string? UserId { get; set; }
    
    [BsonElement("model")]
    [StringLength(200)]
    public string? Model { get; set; }
    
    [BsonElement("prompt_tokens")]
    public long PromptTokens { get; set; }
    
    [BsonElement("completion_tokens")]
    public long CompletionTokens { get; set; }
    
    [BsonElement("total_tokens")]
    public long TotalTokens { get; set; }
    
    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }
    
    [BsonElement("request_id")]
    public string? RequestId { get; set; }
    
    [BsonElement("source")]
    public string? Source { get; set; }
    
    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
    
    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // ========== NEW FIELDS ==========
    
    /// <summary>
    /// Type of usage event: "Tokens", "Messages", "API_Calls", etc.
    /// Default: "Tokens" for backward compatibility
    /// </summary>
    [BsonElement("event_type")]
    [StringLength(50)]
    public string EventType { get; set; } = "Tokens";
    
    /// <summary>
    /// Name of the agent that generated this token usage.
    /// Enables filtering and analytics by agent.
    /// Nullable for backward compatibility.
    /// </summary>
    [BsonElement("agent_name")]
    [StringLength(200)]
    public string? AgentName { get; set; }
    
    /// <summary>
    /// LLM provider: "OpenAI", "Azure", "Anthropic", etc.
    /// </summary>
    [BsonElement("model_provider")]
    [StringLength(50)]
    public string? ModelProvider { get; set; }
    
    /// <summary>
    /// Cost of input/prompt tokens in USD
    /// Nullable if pricing not available
    /// </summary>
    [BsonElement("input_cost_usd")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? InputCostUsd { get; set; }
    
    /// <summary>
    /// Cost of output/completion tokens in USD
    /// Nullable if pricing not available
    /// </summary>
    [BsonElement("output_cost_usd")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? OutputCostUsd { get; set; }
    
    /// <summary>
    /// Total cost (input + output) in USD
    /// Nullable if pricing not available
    /// </summary>
    [BsonElement("total_cost_usd")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal? TotalCostUsd { get; set; }
}
```

**Key Design Decisions:**
- ✅ `EventType` defaults to "Tokens" for backward compatibility
- ✅ `AgentName` enables agent-specific analytics and cost attribution
- ✅ Cost fields are nullable (not all events will have pricing)
- ✅ Uses `Decimal128` for MongoDB to preserve precision
- ✅ `ModelProvider` helps differentiate OpenAI vs Azure pricing

### 2. Create `ModelPricing` Model

**New File:** `Shared/Data/Models/Usage/ModelPricing.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Usage;

/// <summary>
/// Stores pricing information for LLM models.
/// Can be manually configured or synced from external sources (LiteLLM).
/// </summary>
[BsonIgnoreExtraElements]
public class ModelPricing
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    
    /// <summary>
    /// Model identifier (e.g., "gpt-4o", "gpt-4o-mini")
    /// </summary>
    [BsonElement("model_id")]
    [Required]
    [StringLength(200)]
    public required string ModelId { get; set; }
    
    /// <summary>
    /// Provider name: "OpenAI", "Azure", "Anthropic", etc.
    /// </summary>
    [BsonElement("provider")]
    [StringLength(50)]
    public string? Provider { get; set; }
    
    /// <summary>
    /// Cost per 1 million input/prompt tokens in USD
    /// </summary>
    [BsonElement("input_cost_per_million")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal InputCostPerMillion { get; set; }
    
    /// <summary>
    /// Cost per 1 million output/completion tokens in USD
    /// </summary>
    [BsonElement("output_cost_per_million")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal OutputCostPerMillion { get; set; }
    
    /// <summary>
    /// Maximum context window size in tokens
    /// </summary>
    [BsonElement("max_context_tokens")]
    public int? MaxContextTokens { get; set; }
    
    /// <summary>
    /// When this pricing was last updated
    /// </summary>
    [BsonElement("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Source of pricing data: "Manual", "LiteLLM", "OpenRouter"
    /// </summary>
    [BsonElement("source")]
    [StringLength(50)]
    public string Source { get; set; } = "Manual";
    
    /// <summary>
    /// Optional: Start date when this pricing becomes effective
    /// </summary>
    [BsonElement("effective_from")]
    public DateTime? EffectiveFrom { get; set; }
    
    /// <summary>
    /// Optional: End date when this pricing expires
    /// Null means currently active
    /// </summary>
    [BsonElement("effective_to")]
    public DateTime? EffectiveTo { get; set; }
}
```

### 3. MongoDB Indexes

**Collection:** `token_usage_events`

```javascript
// Primary query pattern: filter by tenant, user, event type, date range
db.token_usage_events.createIndex({
  "tenant_id": 1,
  "user_id": 1,
  "event_type": 1,
  "created_at": -1
});

// For tenant-level queries without user filter
db.token_usage_events.createIndex({
  "tenant_id": 1,
  "event_type": 1,
  "created_at": -1
});

// For model-based analytics
db.token_usage_events.createIndex({
  "tenant_id": 1,
  "model": 1,
  "created_at": -1
});

// For agent-based analytics
db.token_usage_events.createIndex({
  "tenant_id": 1,
  "agent_name": 1,
  "created_at": -1
});

// TTL index for automatic cleanup (optional)
db.token_usage_events.createIndex(
  { "created_at": 1 },
  { expireAfterSeconds: 2592000 }  // 30 days
);
```

**Collection:** `model_pricing`

```javascript
// Unique constraint on model_id + provider + date range
db.model_pricing.createIndex({
  "model_id": 1,
  "provider": 1,
  "effective_from": 1
}, { unique: true });

// For quick lookups of current pricing
db.model_pricing.createIndex({
  "model_id": 1,
  "effective_to": 1
});
```

---

## Backend API Requirements

### 1. New Repository: `IModelPricingRepository`

**File:** `Shared/Repositories/ModelPricingRepository.cs`

```csharp
public interface IModelPricingRepository
{
    Task<ModelPricing?> GetCurrentPricingAsync(string modelId, CancellationToken cancellationToken = default);
    Task<List<ModelPricing>> GetAllActivePricingAsync(CancellationToken cancellationToken = default);
    Task<ModelPricing> UpsertAsync(ModelPricing pricing, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
```

### 2. New Service: `IModelPricingService`

**File:** `Shared/Services/ModelPricingService.cs`

**Purpose:** Calculate costs based on token counts and model pricing

```csharp
public interface IModelPricingService
{
    /// <summary>
    /// Calculate cost for a given model and token counts
    /// Returns null if pricing not available for model
    /// </summary>
    Task<CostBreakdown?> CalculateCostAsync(string modelId, long promptTokens, long completionTokens, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sync pricing data from external source (LiteLLM GitHub)
    /// </summary>
    Task SyncPricingFromLiteLLMAsync(CancellationToken cancellationToken = default);
}

public record CostBreakdown(
    decimal InputCost,
    decimal OutputCost,
    decimal TotalCost,
    string ModelId,
    string? Provider);
```

### 3. Update `TokenUsageRecord`

**File:** `Shared/Services/TokenUsageService.cs`

**Current Definition:**
```csharp
public record TokenUsageRecord(
    string TenantId,
    string UserId,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    string? WorkflowId,
    string? RequestId,
    string? Source,
    Dictionary<string, string>? Metadata);
```

**Updated Definition:**
```csharp
public record TokenUsageRecord(
    string TenantId,
    string UserId,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    string? WorkflowId,
    string? RequestId,
    string? Source,
    Dictionary<string, string>? Metadata,
    string? AgentName);  // NEW: Add agent name parameter
```

**Rationale:**
- Captures which agent generated the token usage
- Flows through to `TokenUsageEvent` for persistent storage
- Enables agent-based filtering and analytics

### 4. Update `TokenUsageService.RecordAsync()`

**File:** `Shared/Services/TokenUsageService.cs`

**Modification:**

```csharp
public async Task RecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default)
{
    if (!_options.Enabled)
    {
        return;
    }

    var totalTokens = Math.Max(0, record.PromptTokens) + Math.Max(0, record.CompletionTokens);
    if (totalTokens == 0)
    {
        return;
    }

    var (effectiveLimit, windowStart) = await ResolveLimitAsync(record.TenantId, record.UserId, cancellationToken);
    if (effectiveLimit == null)
    {
        return;
    }

    _logger.LogInformation(
        "Recording token usage: tenant={TenantId}, user={UserId}, totalTokens={TotalTokens}, prompt={PromptTokens}, completion={CompletionTokens}, windowStart={WindowStart:o}, windowSeconds={WindowSeconds}",
        record.TenantId,
        record.UserId,
        totalTokens,
        record.PromptTokens,
        record.CompletionTokens,
        windowStart,
        effectiveLimit.WindowSeconds);

    await _windowRepository.IncrementWindowAsync(
        record.TenantId,
        record.UserId,
        windowStart,
        effectiveLimit.WindowSeconds,
        totalTokens,
        cancellationToken);

    if (_options.RecordUsageEvents)
    {
        // ========== NEW: Calculate cost ==========
        CostBreakdown? costBreakdown = null;
        if (!string.IsNullOrEmpty(record.Model))
        {
            costBreakdown = await _pricingService.CalculateCostAsync(
                record.Model,
                record.PromptTokens,
                record.CompletionTokens,
                cancellationToken);
        }
        
        var usageEvent = new TokenUsageEvent
        {
            TenantId = record.TenantId,
            UserId = record.UserId,
            Model = record.Model,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens = totalTokens,
            WorkflowId = record.WorkflowId,
            RequestId = record.RequestId,
            Source = record.Source,
            Metadata = record.Metadata,
            CreatedAt = DateTime.UtcNow,
            
            // ========== NEW FIELDS ==========
            EventType = "Tokens",
            AgentName = record.AgentName,  // NEW: Include agent name
            ModelProvider = costBreakdown?.Provider,
            InputCostUsd = costBreakdown?.InputCost,
            OutputCostUsd = costBreakdown?.OutputCost,
            TotalCostUsd = costBreakdown?.TotalCost
        };

        await _eventRepository.InsertAsync(usageEvent, cancellationToken);
    }
}
```

### 5. New Analytics Endpoint

**File:** `Features/WebApi/Endpoints/UsageAnalyticsEndpoints.cs`

```csharp
public static class UsageAnalyticsEndpoints
{
    public static void MapUsageAnalytics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/client/usage/analytics")
            .WithTags("Usage Analytics")
            .RequireAuthorization();
        
        // GET /api/client/usage/analytics/events
        group.MapGet("/events", GetUsageEvents);
        
        // GET /api/client/usage/analytics/summary
        group.MapGet("/summary", GetUsageSummary);
        
        // GET /api/client/usage/analytics/timeseries
        group.MapGet("/timeseries", GetUsageTimeSeries);
    }
    
    private static async Task<IResult> GetUsageEvents(
        [FromQuery] string tenantId,
        [FromQuery] string? userId,
        [FromQuery] string? agentName,
        [FromQuery] string? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        ITokenUsageEventRepository eventRepo)
    {
        var events = await eventRepo.GetEventsAsync(
            tenantId, userId, agentName, eventType, from, to, page, pageSize);
        return Results.Ok(events);
    }
    
    private static async Task<IResult> GetUsageSummary(
        [FromQuery] string tenantId,
        [FromQuery] string? userId,
        [FromQuery] string? agentName,
        [FromQuery] string? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        IUsageAnalyticsService analyticsService)
    {
        var summary = await analyticsService.GetSummaryAsync(
            tenantId, userId, agentName, eventType, from, to);
        return Results.Ok(summary);
    }
    
    private static async Task<IResult> GetUsageTimeSeries(
        [FromQuery] string tenantId,
        [FromQuery] string? userId,
        [FromQuery] string? agentName,
        [FromQuery] string? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string aggregation = "day",
        IUsageAnalyticsService analyticsService)
    {
        var timeSeries = await analyticsService.GetTimeSeriesAsync(
            tenantId, userId, agentName, eventType, from, to, aggregation);
        return Results.Ok(timeSeries);
    }
}
```

### 6. New Service: `IUsageAnalyticsService`

**File:** `Shared/Services/UsageAnalyticsService.cs`

**Purpose:** Aggregate and transform event data for dashboard consumption

```csharp
public interface IUsageAnalyticsService
{
    Task<UsageSummary> GetSummaryAsync(
        string tenantId,
        string? userId,
        string? agentName,
        string? eventType,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);
    
    Task<List<TimeSeriesDataPoint>> GetTimeSeriesAsync(
        string tenantId,
        string? userId,
        string? agentName,
        string? eventType,
        DateTime? from,
        DateTime? to,
        string aggregation,
        CancellationToken cancellationToken = default);
}

public record UsageSummary(
    long TotalTokens,
    long TotalEvents,
    decimal? TotalCost,
    long AverageTokensPerEvent,
    List<ModelBreakdown> TopModels,
    List<SourceBreakdown> TopSources,
    List<UserBreakdown> TopUsers,
    List<AgentBreakdown> TopAgents);

public record ModelBreakdown(
    string Model,
    long Tokens,
    int Events,
    decimal? Cost);

public record SourceBreakdown(
    string Source,
    long Tokens,
    int Events);

public record UserBreakdown(
    string UserId,
    long Tokens,
    int Events,
    decimal? Cost);

public record AgentBreakdown(
    string AgentName,
    long Tokens,
    int Events,
    decimal? Cost);

public record TimeSeriesDataPoint(
    DateTime Timestamp,
    long Tokens,
    int Events,
    decimal? Cost);
```

---

## Frontend Components

### 1. New Route

**File:** `src/modules/Manager/routes.js`

```javascript
{
  path: '/usage-analytics',
  element: <UsageAnalyticsDashboard />,
  label: 'Usage Analytics',
  icon: <AnalyticsIcon />,
  requiredRole: 'admin'  // or tenant admin
}
```

### 2. Main Dashboard Component

**File:** `src/modules/Manager/Components/Admin/Analytics/UsageAnalyticsDashboard.jsx`

**Structure:**
```jsx
const UsageAnalyticsDashboard = () => {
  // State
  const [tenantId, setTenantId] = useState('');
  const [userId, setUserId] = useState(null);
  const [agentName, setAgentName] = useState(null);
  const [dateRange, setDateRange] = useState({ from: ..., to: ... });
  const [metricType, setMetricType] = useState('Tokens');
  const [summary, setSummary] = useState(null);
  const [timeSeries, setTimeSeries] = useState([]);
  
  // API calls
  const analyticsApi = useAnalyticsApi();
  
  return (
    <PageLayout title="Usage Analytics">
      {/* Filters */}
      <FilterBar />
      
      {/* Summary Cards */}
      <SummaryCards summary={summary} />
      
      {/* Time Series Chart */}
      <TimeSeriesChart data={timeSeries} />
      
      {/* Breakdown Sections */}
      <BreakdownGrid summary={summary} />
      
      {/* Data Table */}
      <EventsDataTable />
    </PageLayout>
  );
};
```

### 3. Supporting Components

**Filter Bar:**
```jsx
// src/modules/Manager/Components/Admin/Analytics/FilterBar.jsx
const FilterBar = ({ onFilterChange }) => (
  <Paper>
    <Stack direction="row" spacing={2}>
      <TenantSelector />
      <UserSelector />
      <AgentSelector />
      <DateRangePicker />
      <MetricTypeToggle />
    </Stack>
  </Paper>
);
```

**Summary Cards:**
```jsx
// src/modules/Manager/Components/Admin/Analytics/SummaryCards.jsx
const SummaryCards = ({ summary }) => (
  <Grid container spacing={2}>
    <Grid item xs={12} md={3}>
      <MetricCard
        title="Total Tokens"
        value={summary.totalTokens}
        icon={<TokenIcon />}
      />
    </Grid>
    <Grid item xs={12} md={3}>
      <MetricCard
        title="Total Cost"
        value={`$${summary.totalCost?.toFixed(2)}`}
        icon={<DollarIcon />}
      />
    </Grid>
    {/* ... more cards */}
  </Grid>
);
```

**Time Series Chart:**
```jsx
// src/modules/Manager/Components/Admin/Analytics/TimeSeriesChart.jsx
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer } from 'recharts';

const TimeSeriesChart = ({ data, metricType }) => (
  <Paper>
    <ResponsiveContainer width="100%" height={300}>
      <LineChart data={data}>
        <XAxis dataKey="timestamp" />
        <YAxis />
        <Tooltip />
        <Line type="monotone" dataKey="tokens" stroke="#8884d8" />
        {metricType === 'cost' && (
          <Line type="monotone" dataKey="cost" stroke="#82ca9d" />
        )}
      </LineChart>
    </ResponsiveContainer>
  </Paper>
);
```

### 4. New API Hook

**File:** `src/modules/Manager/services/analytics-api.js`

```javascript
import { useMemo } from 'react';
import { useApiClient } from './api-client';

export const useAnalyticsApi = () => {
  const api = useApiClient();

  return useMemo(() => ({
    async getSummary({ tenantId, userId, agentName, eventType, from, to }) {
      return await api.get('/api/client/usage/analytics/summary', {
        tenantId,
        userId,
        agentName,
        eventType,
        from: from?.toISOString(),
        to: to?.toISOString(),
      });
    },

    async getTimeSeries({ tenantId, userId, agentName, eventType, from, to, aggregation }) {
      return await api.get('/api/client/usage/analytics/timeseries', {
        tenantId,
        userId,
        agentName,
        eventType,
        from: from?.toISOString(),
        to: to?.toISOString(),
        aggregation,
      });
    },

    async getEvents({ tenantId, userId, agentName, eventType, from, to, page, pageSize }) {
      return await api.get('/api/client/usage/analytics/events', {
        tenantId,
        userId,
        agentName,
        eventType,
        from: from?.toISOString(),
        to: to?.toISOString(),
        page,
        pageSize,
      });
    },

    async exportToCsv({ tenantId, userId, agentName, eventType, from, to }) {
      const blob = await api.getBlob('/api/client/usage/analytics/export', {
        tenantId,
        userId,
        agentName,
        eventType,
        from: from?.toISOString(),
        to: to?.toISOString(),
        format: 'csv',
      });
      
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `usage-export-${Date.now()}.csv`;
      a.click();
    },
  }), [api]);
};
```

---

## Cost Tracking Strategy

### Problem Statement
Neither OpenAI nor Azure OpenAI provide pricing information in their API responses. We must calculate costs ourselves.

### Solution: Hybrid Pricing Approach

#### Phase 1: Static Configuration (Immediate)

**Configuration File:** `appsettings.json`

```json
{
  "ModelPricing": {
    "Enabled": true,
    "DefaultModels": {
      "gpt-4o": {
        "inputCostPerMillion": 2.50,
        "outputCostPerMillion": 10.00,
        "provider": "OpenAI",
        "lastUpdated": "2024-11-01"
      },
      "gpt-4o-mini": {
        "inputCostPerMillion": 0.15,
        "outputCostPerMillion": 0.60,
        "provider": "OpenAI",
        "lastUpdated": "2024-11-01"
      },
      "gpt-4-turbo": {
        "inputCostPerMillion": 10.00,
        "outputCostPerMillion": 30.00,
        "provider": "OpenAI",
        "lastUpdated": "2024-05-01"
      },
      "gpt-3.5-turbo": {
        "inputCostPerMillion": 0.50,
        "outputCostPerMillion": 1.50,
        "provider": "OpenAI",
        "lastUpdated": "2024-01-01"
      }
    }
  }
}
```

#### Phase 2: Database Storage with Admin UI

**MongoDB Collection:** `model_pricing`

**Admin UI:** Add section in `/manager/admin` to:
- View current pricing
- Add/edit model pricing
- See last updated timestamp
- Mark pricing as active/inactive

#### Phase 3: Automated Sync from LiteLLM (Optional)

**Background Service:**

```csharp
public class ModelPricingSyncHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ModelPricingSyncHostedService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var pricingService = scope.ServiceProvider.GetRequiredService<IModelPricingService>();
                
                await pricingService.SyncPricingFromLiteLLMAsync(stoppingToken);
                
                _logger.LogInformation("Model pricing synced successfully from LiteLLM");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync model pricing from LiteLLM");
            }
            
            await Task.Delay(_syncInterval, stoppingToken);
        }
    }
}
```

**LiteLLM Data Source:**
```
https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json
```

**Benefits:**
- ✅ Community-maintained pricing (100+ models)
- ✅ Regular updates
- ✅ Free to use

**Fallback Logic:**
1. Check database for custom pricing
2. Use config file pricing
3. Return `null` if model not found

---

## Implementation Phases

### Phase 1: Data Model & Cost Tracking Foundation (Week 1)

**Backend Tasks:**
- [ ] Add `EventType`, `ModelProvider`, cost fields to `TokenUsageEvent`
- [ ] Create `ModelPricing` model and repository
- [ ] Implement `ModelPricingService` with calculation logic
- [ ] Update `TokenUsageService.RecordAsync()` to calculate and store costs
- [ ] Add MongoDB indexes for performance
- [ ] Create seed script for initial model pricing
- [ ] Write unit tests for pricing service

**Configuration:**
- [ ] Add `ModelPricing` section to `appsettings.json`
- [ ] Document manual pricing update process

**Deliverable:** Events are now saved with cost data (where available)

---

### Phase 2: Analytics Backend APIs (Week 2)

**Backend Tasks:**
- [ ] Create `ITokenUsageEventRepository.GetEventsAsync()` with filtering
- [ ] Implement `UsageAnalyticsService` with aggregation logic
- [ ] Create `/api/client/usage/analytics/*` endpoints
- [ ] Add pagination support
- [ ] Implement time series aggregation (hourly/daily/weekly)
- [ ] Write integration tests for analytics endpoints

**Deliverable:** Backend APIs ready for frontend consumption

---

### Phase 3: Analytics Dashboard UI (Week 3)

**Frontend Tasks:**
- [ ] Create `useAnalyticsApi` hook
- [ ] Build `UsageAnalyticsDashboard` main component
- [ ] Implement `FilterBar` with tenant/user/date selectors
- [ ] Create `SummaryCards` component
- [ ] Build `TimeSeriesChart` with Recharts
- [ ] Implement breakdown visualizations (model, source, user)
- [ ] Add `EventsDataTable` with pagination
- [ ] Style components to match existing theme

**Deliverable:** Fully functional analytics dashboard

---

### Phase 4: Export & Polish (Week 4)

**Tasks:**
- [ ] Implement CSV export endpoint (`/api/client/usage/analytics/export`)
- [ ] Add export button to dashboard
- [ ] Implement caching for performance (Redis/memory)
- [ ] Add loading skeletons and error states
- [ ] Performance testing with large datasets
- [ ] Documentation updates (user guide, API docs)
- [ ] Add feature flag for gradual rollout

**Deliverable:** Production-ready feature with documentation

---

### Phase 5: Model Pricing Admin UI (Week 5 - Optional)

**Tasks:**
- [ ] Create `/manager/admin/pricing` page
- [ ] Build CRUD UI for model pricing
- [ ] Implement pricing history view
- [ ] Add alerts for pricing changes
- [ ] (Optional) Implement LiteLLM sync background job

**Deliverable:** Self-service pricing management

---

### Future: Message Tracking (Post-MVP)

**When Implemented:**
- [ ] Add message events to `token_usage_events` with `EventType = "Messages"`
- [ ] Track message counts in workflows
- [ ] Update dashboard to support "Messages" metric type
- [ ] Add message-specific visualizations

**Data Structure:**
```javascript
{
  "event_type": "Messages",
  "message_count": 1,
  "message_direction": "Incoming" | "Outgoing",
  "message_bytes": 1024,
  "workflow_id": "...",
  // No token fields for messages
}
```

---

## Performance Considerations

### 1. Query Optimization

**Problem:** Large datasets (millions of events) can slow down queries.

**Solutions:**
- ✅ Use MongoDB aggregation pipeline for summarization
- ✅ Create compound indexes on common query patterns
- ✅ Limit date range to reasonable defaults (7-30 days)
- ✅ Paginate event results (50-100 per page)

### 2. Pre-aggregation Strategy

**For very large tenants:**
- Create background job to pre-aggregate events into hourly/daily summaries
- Store in separate `usage_aggregates` collection
- Dashboard queries hit aggregates instead of raw events
- Rebuild aggregates when needed (e.g., pricing updates)

**Schema:**
```javascript
{
  "tenant_id": "string",
  "user_id": "string?",
  "event_type": "Tokens",
  "period": "2025-11-28T00:00:00Z",
  "granularity": "day",  // "hour", "day", "week"
  "total_tokens": 125000,
  "total_events": 543,
  "total_cost": 52.35,
  "by_model": {
    "gpt-4o": { tokens: 80000, events: 320, cost: 35.20 },
    "gpt-4o-mini": { tokens: 45000, events: 223, cost: 17.15 }
  },
  "by_source": {
    "SemanticRouter.Route": { tokens: 90000, events: 400 }
  }
}
```

### 3. Caching Strategy

**Cache Key Pattern:**
```
usage:summary:{tenantId}:{userId}:{eventType}:{from}:{to}
usage:timeseries:{tenantId}:{userId}:{eventType}:{from}:{to}:{aggregation}
```

**TTL:** 5-10 minutes (balance freshness vs performance)

**Invalidation:** On new usage events (eventually consistent)

### 4. Data Retention

**Configuration:**
```json
{
  "TokenUsage": {
    "MaxUsageHistoryDays": 30,
    "AutoDeleteOldEvents": true
  }
}
```

**MongoDB TTL Index:**
```javascript
db.token_usage_events.createIndex(
  { "created_at": 1 },
  { expireAfterSeconds: 2592000 }  // 30 days
);
```

**Archive Strategy (Future):**
- Export old events to cold storage (S3, Azure Blob)
- Keep aggregates longer than raw events
- Allow admin to request archived data

---

## Future Enhancements

### 1. Cost Alerts & Budgets

**Features:**
- Set monthly/weekly cost budgets per tenant
- Email/Slack notifications when approaching limit
- Dashboard widget showing budget vs actual

### 2. Predictive Analytics

**Features:**
- Forecast future usage based on trends
- Predict when quota will be exceeded
- Recommend optimal quota settings

### 3. Multi-Tenant Comparison

**Features:**
- Compare usage across tenants (admin view)
- Identify outliers and optimization opportunities
- Benchmark against averages

### 4. Advanced Filters

**Features:**
- Filter by workflow type
- Filter by participant ID
- Custom date ranges (relative: "last 7 days", "this month")

### 5. Scheduled Reports

**Features:**
- Weekly/monthly email reports
- PDF generation
- Automated export to Google Sheets / Excel

### 6. Real-time Usage Monitoring

**Features:**
- WebSocket updates for live usage
- Real-time cost tracking
- Alert when unusual patterns detected

---

## Related Documentation

- [TOKEN_LIMITING_ARCHITECTURE.md](./TOKEN_LIMITING_ARCHITECTURE.md) - Current token limiting system
- [TOKEN_LIMITING_IMPLEMENTATION_PLAN.md](./TOKEN_LIMITING_IMPLEMENTATION_PLAN.md) - Original implementation plan
- [TOKEN_LIMITING_PHASES.md](./TOKEN_LIMITING_PHASES.md) - Delivery phases
- [usage-management.md](../XiansAi.UI/docs/nonfunctional/usage-management.md) - Current UI documentation

---

## Revision History

- **2025-11-28**: Initial planning document created based on requirements analysis
  - Analyzed current state of token tracking
  - Designed analytics dashboard UI/UX
  - Defined data model extensions for event types and cost tracking
  - Documented cost tracking strategy (LiteLLM sync)
  - Outlined implementation phases

---

## Approval Checklist

- [ ] **Product Owner Review**: Feature requirements and UX approved
- [ ] **Tech Lead Review**: Architecture and data model approved
- [ ] **Security Review**: API authorization and data privacy concerns addressed
- [ ] **Performance Review**: Scalability considerations documented
- [ ] **Documentation Review**: Implementation plan is clear and complete

---

**Status:** 📝 **Draft - Awaiting Approval**

**Next Steps:**
1. Review this document with stakeholders
2. Prioritize implementation phases
3. Create detailed task breakdown in project management tool
4. Begin Phase 1 implementation
