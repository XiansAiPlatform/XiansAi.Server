# Admin Metrics Service

## Implementation Status: 🚧 SPECIFICATION

### Overview

The Admin Metrics Service provides comprehensive performance analytics for agents by analyzing usage metrics from the `usage_metrics` collection. It offers detailed insights into agent performance, resource consumption, response times, and trends over time.

## Endpoints

### 1. GET /api/v1/admin/tenants/{tenantId}/metrics/stats

**Status:** 📋 Specification

**Purpose:** Get aggregated statistics for all metrics, dynamically grouped by discovered categories and types.

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (optional): Filter by specific activation name (query parameter)
- `participantId` (optional): Filter by participant user ID (query parameter)
- `workflowType` (optional): Filter by workflow type (query parameter)
- `model` (optional): Filter by AI model name (query parameter)

**Response:**

```json
{
  "period": {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "filters": {
    "agentName": "CustomerSupportAgent",
    "activationName": null,
    "participantId": null,
    "workflowType": null,
    "model": null
  },
  "summary": {
    "totalMetricRecords": 6840,
    "uniqueCategories": 4,
    "uniqueTypes": 12,
    "uniqueActivations": 5,
    "uniqueParticipants": 25,
    "uniqueWorkflows": 68,
    "uniqueModels": 2,
    "dateRange": {
      "earliest": "2026-01-01T08:23:45Z",
      "latest": "2026-01-31T18:45:12Z"
    }
  },
  "categoriesAndTypes": [
    {
      "category": "tokens",
      "types": [
        {
          "type": "prompt_tokens",
          "stats": {
            "count": 1234,
            "sum": 234567.0,
            "average": 190.1,
            "min": 45.0,
            "max": 2500.0,
            "unit": "tokens"
          }
        },
        {
          "type": "completion_tokens",
          "stats": {
            "count": 1234,
            "sum": 222222.0,
            "average": 180.1,
            "min": 30.0,
            "max": 1800.0,
            "unit": "tokens"
          }
        },
        {
          "type": "total_tokens",
          "stats": {
            "count": 1234,
            "sum": 456789.0,
            "average": 370.2,
            "min": 75.0,
            "max": 4300.0,
            "unit": "tokens"
          }
        }
      ]
    },
    {
      "category": "performance",
      "types": [
        {
          "type": "response_time",
          "stats": {
            "count": 1234,
            "sum": 1536867.0,
            "average": 1245.5,
            "min": 450.0,
            "max": 8900.0,
            "median": 1100.0,
            "p95": 2300.0,
            "p99": 3500.0,
            "unit": "ms"
          }
        },
        {
          "type": "processing_time",
          "stats": {
            "count": 1234,
            "sum": 890234.0,
            "average": 721.6,
            "min": 200.0,
            "max": 5000.0,
            "unit": "ms"
          }
        }
      ]
    },
    {
      "category": "cost",
      "types": [
        {
          "type": "api_cost",
          "stats": {
            "count": 1234,
            "sum": 12.34,
            "average": 0.01,
            "min": 0.001,
            "max": 0.15,
            "unit": "usd"
          }
        }
      ]
    },
    {
      "category": "quality",
      "types": [
        {
          "type": "success_rate",
          "stats": {
            "count": 1234,
            "sum": 1215.0,
            "average": 98.5,
            "min": 0.0,
            "max": 100.0,
            "unit": "percentage"
          }
        }
      ]
    }
  ],
  "byActivation": [
    {
      "activationName": "email-responder",
      "metricCount": 4500,
      "categoriesAndTypes": [
        {
          "category": "tokens",
          "types": [
            {
              "type": "total_tokens",
              "stats": {
                "count": 450,
                "sum": 156789.0,
                "average": 348.4,
                "unit": "tokens"
              }
            }
          ]
        },
        {
          "category": "performance",
          "types": [
            {
              "type": "response_time",
              "stats": {
                "count": 450,
                "sum": 495225.0,
                "average": 1100.5,
                "unit": "ms"
              }
            }
          ]
        }
      ]
    },
    {
      "activationName": "chat-responder",
      "metricCount": 2340,
      "categoriesAndTypes": [
        {
          "category": "tokens",
          "types": [
            {
              "type": "total_tokens",
              "stats": {
                "count": 234,
                "sum": 89012.0,
                "average": 380.4,
                "unit": "tokens"
              }
            }
          ]
        },
        {
          "category": "performance",
          "types": [
            {
              "type": "response_time",
              "stats": {
                "count": 234,
                "sum": 229366.8,
                "average": 980.2,
                "unit": "ms"
              }
            }
          ]
        }
      ]
    }
  ]
}
```

**Use Cases:**
- Agent-specific dashboard overview
- View all metrics for a specific agent
- Aggregate statistics by activation within an agent
- Analyze agent performance across different activations

---

### 2. GET /api/v1/admin/tenants/{tenantId}/metrics/timeseries

**Status:** 📋 Specification

**Purpose:** Get time-series data for specific metric category/type combinations, with flexible grouping and aggregation.

**Parameters:**

- `startDate` (required): Start of date range (ISO 8601 format, query parameter)
- `endDate` (required): End of date range (ISO 8601 format, query parameter)
- `category` (required): Metric category to analyze (query parameter)
- `type` (required): Metric type within the category (query parameter)
- `agentName` (required): Filter by specific agent name (query parameter)
- `activationName` (optional): Filter by specific activation name (query parameter)
- `participantId` (optional): Filter by participant user ID (query parameter)
- `workflowType` (optional): Filter by workflow type (query parameter)
- `model` (optional): Filter by AI model name (query parameter)
- `groupBy` (optional): Time granularity - "day", "week", "month" (default: "day") (query parameter)
- `aggregation` (optional): Aggregation method - "sum", "avg", "min", "max", "count" (default: "sum") (query parameter)
- `includeBreakdowns` (optional): Include breakdowns by activation (default: false) (query parameter)

**Response:**

```json
{
  "period": {
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "metric": {
    "category": "tokens",
    "type": "total_tokens",
    "unit": "tokens"
  },
  "filters": {
    "agentName": "CustomerSupportAgent",
    "activationName": null,
    "participantId": null,
    "workflowType": null,
    "model": null
  },
  "groupBy": "day",
  "aggregation": "sum",
  "dataPoints": [
    {
      "timestamp": "2026-01-01T00:00:00Z",
      "value": 12345.0,
      "count": 45,
      "breakdowns": {
        "byActivation": [
          {
            "dimension": "email-responder",
            "value": 7000.0,
            "count": 25
          },
          {
            "dimension": "chat-responder",
            "value": 5345.0,
            "count": 20
          }
        ]
      }
    },
    {
      "timestamp": "2026-01-02T00:00:00Z",
      "value": 13567.0,
      "count": 48,
      "breakdowns": {
        "byActivation": [
          {
            "dimension": "email-responder",
            "value": 7800.0,
            "count": 28
          },
          {
            "dimension": "chat-responder",
            "value": 5767.0,
            "count": 20
          }
        ]
      }
    }
  ],
  "summary": {
    "totalValue": 456789.0,
    "totalCount": 1234,
    "average": 370.1,
    "min": 12345.0,
    "max": 18900.0,
    "dataPointCount": 31
  }
}
```

**Use Cases:**

- Time-series charts for agent-specific metrics
- Trend analysis for a specific agent over time
- Track agent performance day-over-day, week-over-week
- Visualize metric evolution by activation within an agent

---

### 3. GET /api/v1/admin/tenants/{tenantId}/metrics/categories

**Status:** 📋 Specification

**Purpose:** Discover available metric categories and types in the tenant's data.

**Parameters:**

- `startDate` (optional): Filter to categories/types available since this date (query parameter)
- `endDate` (optional): Filter to categories/types available until this date (query parameter)
- `agentName` (optional): Filter to categories/types for specific agent (query parameter)

**Response:**

```json
{
  "dateRange": {
    "startDate": "2025-12-01T00:00:00Z",
    "endDate": "2026-01-31T23:59:59Z"
  },
  "categories": [
    {
      "category": "tokens",
      "types": [
        {
          "type": "prompt_tokens",
          "sampleCount": 1234,
          "units": ["tokens"],
          "firstSeen": "2025-12-01T08:23:45Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent", "SalesAgent"],
          "sampleValue": 234567.0
        },
        {
          "type": "completion_tokens",
          "sampleCount": 1234,
          "units": ["tokens"],
          "firstSeen": "2025-12-01T08:23:45Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent", "SalesAgent"],
          "sampleValue": 222222.0
        },
        {
          "type": "total_tokens",
          "sampleCount": 1234,
          "units": ["tokens"],
          "firstSeen": "2025-12-01T08:23:45Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent", "SalesAgent"],
          "sampleValue": 456789.0
        }
      ],
      "totalMetrics": 3,
      "totalRecords": 3702
    },
    {
      "category": "performance",
      "types": [
        {
          "type": "response_time",
          "sampleCount": 1234,
          "units": ["ms"],
          "firstSeen": "2025-12-01T08:23:45Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent", "SalesAgent"],
          "sampleValue": 1245.5
        },
        {
          "type": "processing_time",
          "sampleCount": 1234,
          "units": ["ms"],
          "firstSeen": "2025-12-15T10:00:00Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent"],
          "sampleValue": 721.6
        },
        {
          "type": "queue_time",
          "sampleCount": 890,
          "units": ["ms"],
          "firstSeen": "2026-01-01T00:00:00Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent"],
          "sampleValue": 45.2
        }
      ],
      "totalMetrics": 3,
      "totalRecords": 3358
    },
    {
      "category": "cost",
      "types": [
        {
          "type": "api_cost",
          "sampleCount": 1234,
          "units": ["usd"],
          "firstSeen": "2025-12-15T00:00:00Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent", "SalesAgent"],
          "sampleValue": 12.34
        }
      ],
      "totalMetrics": 1,
      "totalRecords": 1234
    },
    {
      "category": "quality",
      "types": [
        {
          "type": "success_rate",
          "sampleCount": 1234,
          "units": ["percentage", "%"],
          "firstSeen": "2026-01-01T00:00:00Z",
          "lastSeen": "2026-01-31T18:45:12Z",
          "agents": ["CustomerSupportAgent"],
          "sampleValue": 98.5
        }
      ],
      "totalMetrics": 1,
      "totalRecords": 1234
    }
  ],
  "summary": {
    "totalCategories": 4,
    "totalTypes": 8,
    "totalRecords": 9528,
    "availableAgents": ["CustomerSupportAgent", "SalesAgent"],
    "dateRange": {
      "earliest": "2025-12-01T08:23:45Z",
      "latest": "2026-01-31T18:45:12Z"
    }
  }
}
```

**Use Cases:**

- Dynamically build UI metric selectors
- Discover what metrics are being tracked
- Show available metrics for specific agents
- API documentation and exploration

---

## Implementation Details

### Data Source

All endpoints query the `usage_metrics` MongoDB collection which stores flattened metric documents:

```csharp
public class UsageMetric
{
    public string TenantId { get; set; }
    public string? ParticipantId { get; set; }
    public string? AgentName { get; set; }
    public string? ActivationName { get; set; }
    public string? WorkflowId { get; set; }
    public string? RequestId { get; set; }
    public string? WorkflowType { get; set; }
    public string? Model { get; set; }
    public string Category { get; set; }      // "tokens", "performance", "cost", etc.
    public string Type { get; set; }          // "prompt_tokens", "response_time", etc.
    public double Value { get; set; }
    public string? Unit { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### Required MongoDB Indexes

For optimal query performance, create these indexes on the `usage_metrics` collection:

```yaml
# Primary index for stats endpoint with all filters
- collection: usage_metrics
  name: idx_stats_queries
  keys:
    tenant_id: 1
    created_at: 1
    category: 1
    type: 1
    agent_name: 1
    activation_name: 1

# Index for time-series queries (category/type first for selectivity)
- collection: usage_metrics
  name: idx_timeseries_queries
  keys:
    tenant_id: 1
    category: 1
    type: 1
    created_at: 1

# Index for category discovery queries
- collection: usage_metrics
  name: idx_category_discovery
  keys:
    tenant_id: 1
    category: 1
    type: 1

# Index for agent-filtered queries
- collection: usage_metrics
  name: idx_agent_metrics
  keys:
    tenant_id: 1
    agent_name: 1
    created_at: 1
    category: 1

# Index for participant-filtered queries
- collection: usage_metrics
  name: idx_participant_metrics
  keys:
    tenant_id: 1
    participant_id: 1
    created_at: 1

# Index for model-filtered queries
- collection: usage_metrics
  name: idx_model_metrics
  keys:
    tenant_id: 1
    model: 1
    created_at: 1

# Index for workflow-type queries
- collection: usage_metrics
  name: idx_workflow_metrics
  keys:
    tenant_id: 1
    workflow_type: 1
    created_at: 1
```

**Index Usage Strategy:**

- `idx_stats_queries`: Primary index for stats endpoint with multiple filters
- `idx_timeseries_queries`: Optimized for time-series queries with specific category/type
- `idx_category_discovery`: Fast category/type enumeration
- `idx_agent_metrics`, `idx_participant_metrics`, `idx_model_metrics`, `idx_workflow_metrics`: Dimension-specific filtering

**Note:** MongoDB's query planner will automatically select the most efficient index based on the query filters.

### Service Architecture

```
AdminMetricsEndpoints (HTTP Layer)
    ↓
AdminMetricsService (Business Logic)
    ↓
UsageMetricRepository (Data Access)
    ↓
MongoDB usage_metrics collection
```

### Aggregation Strategy

#### 1. Stats Endpoint (`/metrics/stats`)

Uses MongoDB aggregation pipeline to dynamically discover and aggregate all metrics:

```javascript
[
  // Step 1: Filter by tenant, agent, and date range
  {
    $match: {
      tenant_id: "tenant123",
      agent_name: "CustomerSupportAgent",  // Required
      created_at: { $gte: startDate, $lte: endDate },
      // ... optional filters (activation_name, participant_id, etc.)
    }
  },
  
  // Step 2: Use $facet for parallel aggregations
  {
    $facet: {
      // Get all unique categories and types
      "categoriesAndTypes": [
        {
          $group: {
            _id: { category: "$category", type: "$type" },
            count: { $sum: 1 },
            sum: { $sum: "$value" },
            avg: { $avg: "$value" },
            min: { $min: "$value" },
            max: { $max: "$value" },
            units: { $addToSet: "$unit" },
            values: { $push: "$value" }  // For percentile calculation
          }
        },
        {
          $group: {
            _id: "$_id.category",
            types: {
              $push: {
                type: "$_id.type",
                stats: {
                  count: "$count",
                  sum: "$sum",
                  average: "$avg",
                  min: "$min",
                  max: "$max",
                  unit: { $arrayElemAt: ["$units", 0] }
                },
                values: "$values"
              }
            }
          }
        },
        {
          $project: {
            category: "$_id",
            types: 1,
            _id: 0
          }
        }
      ],
      
      // Breakdown by activation
      "byActivation": [
        {
          $group: {
            _id: {
              activation: "$activation_name",
              category: "$category",
              type: "$type"
            },
            count: { $sum: 1 },
            sum: { $sum: "$value" },
            avg: { $avg: "$value" },
            unit: { $first: "$unit" }
          }
        },
        {
          $group: {
            _id: "$_id.activation",
            categoriesAndTypes: {
              $push: {
                category: "$_id.category",
                type: "$_id.type",
                stats: {
                  count: "$count",
                  sum: "$sum",
                  average: "$avg",
                  unit: "$unit"
                }
              }
            },
            metricCount: { $sum: "$count" }
          }
        },
        {
          $project: {
            activationName: "$_id",
            categoriesAndTypes: 1,
            metricCount: 1,
            _id: 0
          }
        }
      ],
      
      // Summary statistics
      "summary": [
        {
          $group: {
            _id: null,
            totalMetricRecords: { $sum: 1 },
            uniqueCategories: { $addToSet: "$category" },
            uniqueTypes: { $addToSet: { category: "$category", type: "$type" } },
            uniqueActivations: { $addToSet: "$activation_name" },
            uniqueParticipants: { $addToSet: "$participant_id" },
            uniqueWorkflows: { $addToSet: "$workflow_id" },
            uniqueModels: { $addToSet: "$model" },
            earliest: { $min: "$created_at" },
            latest: { $max: "$created_at" }
          }
        }
      ]
    }
  }
]
```

**Post-processing in C#:**
- Calculate percentiles (p50, p95, p99) from the values array for performance metrics
- Group types under their categories
- Remove nulls from unique sets

#### 2. Time-Series Endpoint (`/metrics/timeseries`)

```javascript
[
  // Step 1: Filter
  {
    $match: {
      tenant_id: "tenant123",
      agent_name: "CustomerSupportAgent",  // Required
      category: "tokens",
      type: "total_tokens",
      created_at: { $gte: startDate, $lte: endDate },
      // ... optional filters (activation_name, participant_id, etc.)
    }
  },
  
  // Step 2: Group by time buckets
  {
    $group: {
      _id: {
        // Time bucketing based on groupBy parameter
        timestamp: {
          $dateTrunc: {
            date: "$created_at",
            unit: "day"  // or "week", "month"
          }
        }
      },
      // Aggregation based on parameter (sum, avg, min, max)
      value: { $sum: "$value" },  // or $avg, $min, $max
      count: { $sum: 1 },
      
      // If includeBreakdowns=true, collect for breakdowns
      records: { $push: "$$ROOT" }
    }
  },
  
  // Step 3: Sort by timestamp
  {
    $sort: { "_id.timestamp": 1 }
  },
  
  // Step 4: If includeBreakdowns, add breakdown calculations
  {
    $facet: {
      "dataPoints": [
        {
          $project: {
            timestamp: "$_id.timestamp",
            value: 1,
            count: 1,
            records: 1
          }
        }
      ],
      "summary": [
        {
          $group: {
            _id: null,
            totalValue: { $sum: "$value" },
            totalCount: { $sum: "$count" },
            average: { $avg: "$value" },
            min: { $min: "$value" },
            max: { $max: "$value" },
            dataPointCount: { $sum: 1 }
          }
        }
      ]
    }
  }
]
```

**Post-processing for breakdowns** (if includeBreakdowns=true):
For each dataPoint, create breakdown by activation:

```javascript
// Breakdown by activation
{
  $unwind: "$records"
},
{
  $group: {
    _id: {
      timestamp: "$timestamp",
      activation: "$records.activation_name"
    },
    value: { $sum: "$records.value" },
    count: { $sum: 1 }
  }
},
{
  $group: {
    _id: "$_id.timestamp",
    byActivation: {
      $push: {
        dimension: "$_id.activation",
        value: "$value",
        count: "$count"
      }
    }
  }
}
```

#### 3. Categories Endpoint (`/metrics/categories`)

```javascript
[
  // Step 1: Filter
  {
    $match: {
      tenant_id: "tenant123",
      // Optional: date range, agent filter
    }
  },
  
  // Step 2: Group by category and type
  {
    $group: {
      _id: {
        category: "$category",
        type: "$type"
      },
      sampleCount: { $sum: 1 },
      units: { $addToSet: "$unit" },
      firstSeen: { $min: "$created_at" },
      lastSeen: { $max: "$created_at" },
      agents: { $addToSet: "$agent_name" },
      sampleValue: { $first: "$value" }
    }
  },
  
  // Step 3: Group types under categories
  {
    $group: {
      _id: "$_id.category",
      types: {
        $push: {
          type: "$_id.type",
          sampleCount: "$sampleCount",
          units: "$units",
          firstSeen: "$firstSeen",
          lastSeen: "$lastSeen",
          agents: "$agents",
          sampleValue: "$sampleValue"
        }
      },
      totalRecords: { $sum: "$sampleCount" }
    }
  },
  
  // Step 4: Project final structure
  {
    $project: {
      category: "$_id",
      types: 1,
      totalMetrics: { $size: "$types" },
      totalRecords: 1,
      _id: 0
    }
  },
  
  // Step 5: Sort by category
  {
    $sort: { category: 1 }
  }
]
```

### Error Handling

- Validate required parameters (tenantId, agentName, startDate, endDate)
- Validate date range (startDate ≤ endDate)
- Validate enum values:
  - groupBy: "day", "week", "month"
  - aggregation: "sum", "avg", "min", "max", "count"
- Return meaningful error messages for:
  - Invalid or non-existent agentName
  - Invalid metric categories/types
  - Invalid date formats
- Handle missing data gracefully with empty arrays/zero values
- Return 404 if agent not found for the tenant
- Comprehensive logging at all levels

### Authorization

- All endpoints require AdminEndpointAuthPolicy
- Tenant isolation enforced at query level
- No cross-tenant data exposure

### Performance Considerations

- Use MongoDB aggregation pipelines for server-side processing
- Implement pagination for large result sets (if needed)
- Cache frequently accessed metric metadata
- Consider pre-aggregating common queries for very large datasets
- Monitor query performance and add indexes as needed

### Future Enhancements

1. Real-time metrics streaming via WebSocket
2. Custom metric definitions and formulas
3. Alerting and threshold monitoring
4. Export to CSV/Excel
5. Scheduled reports
6. Metric retention policies
7. Cost optimization recommendations
8. Anomaly detection algorithms
