using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Usage;

/// <summary>
/// Usage metric model - each metric stored as a separate document for optimal query performance.
/// Replaces the nested UsageEvent design. Stored in 'usage_metrics' collection.
/// </summary>
[BsonIgnoreExtraElements]
public class UsageMetric
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [Required]
    public required string TenantId { get; set; }

    [BsonElement("participant_id")]
    public string? ParticipantId { get; set; }

    [BsonElement("agent_name")]
    public string? AgentName { get; set; }

    [BsonElement("activation_name")]
    public string? ActivationName { get; set; }

    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }

    [BsonElement("request_id")]
    public string? RequestId { get; set; }

    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("model")]
    [StringLength(200)]
    public string? Model { get; set; }

    // Metric fields (flattened from array)
    [BsonElement("category")]
    [Required]
    [StringLength(100)]
    public required string Category { get; set; }

    [BsonElement("type")]
    [Required]
    [StringLength(100)]
    public required string Type { get; set; }

    [BsonElement("value")]
    public double Value { get; set; }

    [BsonElement("unit")]
    [StringLength(50)]
    public string? Unit { get; set; }

    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


/// <summary>
/// Response model for available metrics discovery API.
/// </summary>
public record AvailableMetricsResponse
{
    public required List<MetricCategoryInfo> Categories { get; init; }
}

/// <summary>
/// Information about a metric category.
/// </summary>
public record MetricCategoryInfo
{
    public required string CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required List<MetricDefinition> Metrics { get; init; }
}

/// <summary>
/// Definition of a specific metric type.
/// </summary>
public record MetricDefinition
{
    public required string Type { get; init; }
    public required string DisplayName { get; init; }
    public required string Unit { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Usage type enumeration - kept for backward compatibility in query responses.
/// </summary>
public enum UsageType
{
    Tokens,
    Messages,
    ResponseTime,
    ApiCalls,      // Future extension
    StorageBytes,  // Future extension
    ComputeTime    // Future extension
}

/// <summary>
/// Generic usage events response - works for any usage type.
/// </summary>
public record UsageEventsResponse
{
    public required string TenantId { get; init; }
    public string? ParticipantId { get; init; }  // null = all participants, value = specific participant
    
    // New flexible response fields
    public string? Category { get; init; }
    public string? MetricType { get; init; }
    public string? Unit { get; init; }
    
    // Legacy support
    public UsageType? Type { get; init; }
    
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public long TotalValue { get; init; }
    public required UsageMetrics TotalMetrics { get; init; }
    public required List<TimeSeriesDataPoint> TimeSeriesData { get; init; }
    public required List<UserBreakdown> UserBreakdown { get; init; }
    public required List<AgentBreakdown> AgentBreakdown { get; init; }
    public required List<AgentTimeSeriesDataPoint> AgentTimeSeriesData { get; init; }
}

/// <summary>
/// Generic metrics container - flexible counters for different usage types.
/// Only relevant fields are populated based on the usage type.
/// </summary>
public record UsageMetrics
{
    // Generic counters
    public long PrimaryCount { get; init; }      // tokens, messages, API calls, bytes, etc.
    public int RequestCount { get; init; }       // Number of requests/operations
    
    // Token-specific breakdown (optional)
    public long? PromptCount { get; init; }      // For tokens: prompt tokens
    public long? CompletionCount { get; init; }  // For tokens: completion tokens
    
    // Future extensibility - add new metrics here as needed
    public Dictionary<string, long>? AdditionalMetrics { get; init; }
}

/// <summary>
/// Time series data point - generic for any usage type.
/// </summary>
public record TimeSeriesDataPoint
{
    public DateTime Date { get; init; }
    public required UsageMetrics Metrics { get; init; }
}

/// <summary>
/// Agent-based time series data point - shows usage per agent over time.
/// </summary>
public record AgentTimeSeriesDataPoint
{
    public DateTime Date { get; init; }
    public required string AgentName { get; init; }
    public required UsageMetrics Metrics { get; init; }
}

/// <summary>
/// User-level breakdown - generic for any usage type.
/// </summary>
public record UserBreakdown
{
    public required string ParticipantId { get; init; }
    public string? UserName { get; init; }
    public required UsageMetrics Metrics { get; init; }
}

/// <summary>
/// Agent-level breakdown - generic for any usage type.
/// </summary>
public record AgentBreakdown
{
    public required string AgentName { get; init; }
    public required UsageMetrics Metrics { get; init; }
}

/// <summary>
/// Simple user list item for filter dropdown.
/// </summary>
public record UserListItem
{
    public required string ParticipantId { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// Request parameters for usage events queries.
/// Supports both legacy UsageType enum and new category/type filtering.
/// </summary>
public record UsageEventsRequest
{
    public required string TenantId { get; init; }
    public string? ParticipantId { get; init; }  // null or "all" = all participants
    public string? AgentName { get; init; }  // null or "all" = all agents
    
    // New flexible filtering
    public string? Category { get; init; }  // null = all categories
    public string? MetricType { get; init; }  // specific metric type
    
    // Legacy support (will be mapped to Category/MetricType)
    public UsageType? Type { get; init; }
    
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public string GroupBy { get; init; } = "day";  // "day" | "week" | "month"
}

/// <summary>
/// Request model for flexible metrics endpoint.
/// Supports sending metrics in the new flexible array format.
/// </summary>
public class UsageReportRequest
{
    public string? TenantId { get; set; }
    public string? ParticipantId { get; set; }
    public string? AgentName { get; set; }
    public string? ActivationName { get; set; }
    public string? Model { get; set; }
    public string? WorkflowId { get; set; }
    public string? RequestId { get; set; }
    public string? WorkflowType { get; set; }
    
    [StringLength(50)]
    public string? CustomIdentifier { get; set; }
    
    public Dictionary<string, string>? Metadata { get; set; }
    
    [Required]
    public required List<MetricValueDto> Metrics { get; set; }
}

/// <summary>
/// DTO for metric values in API requests.
/// </summary>
public class MetricValueDto
{
    [Required]
    [StringLength(100)]
    public required string Category { get; set; }
    
    [Required]
    [StringLength(100)]
    public required string Type { get; set; }
    
    public double Value { get; set; }
    
    [StringLength(50)]
    public string? Unit { get; set; }
}

// ============================================================================
// ADMIN METRICS MODELS
// ============================================================================

/// <summary>
/// Request parameters for admin metrics stats endpoint.
/// </summary>
public record AdminMetricsStatsRequest
{
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public string? ActivationName { get; init; }
    public string? ParticipantId { get; init; }
    public string? WorkflowType { get; init; }
    public string? Model { get; init; }
}

/// <summary>
/// Response for admin metrics stats endpoint.
/// </summary>
public record AdminMetricsStatsResponse
{
    public required DateRangeInfo Period { get; init; }
    public required MetricsFilters Filters { get; init; }
    public required MetricsSummary Summary { get; init; }
    public required List<CategoryStats> CategoriesAndTypes { get; init; }
    public required List<ActivationStats> ByActivation { get; init; }
}

/// <summary>
/// Request parameters for admin metrics timeseries endpoint.
/// </summary>
public record AdminMetricsTimeSeriesRequest
{
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }
    public required string Category { get; init; }
    public required string Type { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public string? ActivationName { get; init; }
    public string? ParticipantId { get; init; }
    public string? WorkflowType { get; init; }
    public string? Model { get; init; }
    public string GroupBy { get; init; } = "day";  // "day", "week", "month"
    public string Aggregation { get; init; } = "sum";  // "sum", "avg", "min", "max", "count"
    public bool IncludeBreakdowns { get; init; } = false;
}

/// <summary>
/// Response for admin metrics timeseries endpoint.
/// </summary>
public record AdminMetricsTimeSeriesResponse
{
    public required DateRangeInfo Period { get; init; }
    public required MetricInfo Metric { get; init; }
    public required MetricsFilters Filters { get; init; }
    public required string GroupBy { get; init; }
    public required string Aggregation { get; init; }
    public required List<MetricTimeSeriesDataPoint> DataPoints { get; init; }
    public required TimeSeriesSummary Summary { get; init; }
}/// <summary>
/// Request parameters for admin metrics categories endpoint.
/// </summary>
public record AdminMetricsCategoriesRequest
{
    public required string TenantId { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? AgentName { get; init; }
    public string? ActivationName { get; init; }
}

/// <summary>
/// Response for admin metrics categories discovery endpoint.
/// </summary>
public record AdminMetricsCategoriesResponse
{
    public DateRangeInfo? DateRange { get; init; }
    public required List<CategoryDiscoveryInfo> Categories { get; init; }
    public required CategoriesSummary Summary { get; init; }
}

// Supporting DTOs

public record DateRangeInfo
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
}

public record MetricsFilters
{
    public required string AgentName { get; init; }
    public string? ActivationName { get; init; }
    public string? ParticipantId { get; init; }
    public string? WorkflowType { get; init; }
    public string? Model { get; init; }
}

public record MetricsSummary
{
    public required long TotalMetricRecords { get; init; }
    public required int UniqueCategories { get; init; }
    public required int UniqueTypes { get; init; }
    public required int UniqueActivations { get; init; }
    public required int UniqueParticipants { get; init; }
    public required int UniqueWorkflows { get; init; }
    public required int UniqueModels { get; init; }
    public required DateRangeInfo DateRange { get; init; }
}

public record CategoryStats
{
    public required string Category { get; init; }
    public required List<TypeStats> Types { get; init; }
}

public record TypeStats
{
    public required string Type { get; init; }
    public required MetricStats Stats { get; init; }
}

public record MetricStats
{
    public required long Count { get; init; }
    public required double Sum { get; init; }
    public required double Average { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public double? Median { get; init; }
    public double? P95 { get; init; }
    public double? P99 { get; init; }
    public required string Unit { get; init; }
}

public record ActivationStats
{
    public required string ActivationName { get; init; }
    public required long MetricCount { get; init; }
    public required List<CategoryStats> CategoriesAndTypes { get; init; }
}

public record MetricInfo
{
    public required string Category { get; init; }
    public required string Type { get; init; }
    public required string Unit { get; init; }
}

public record MetricTimeSeriesDataPoint
{
    public required DateTime Timestamp { get; init; }
    public required double Value { get; init; }
    public required long Count { get; init; }
    public MetricTimeSeriesBreakdowns? Breakdowns { get; init; }
}

public record MetricTimeSeriesBreakdowns
{
    public List<BreakdownItem>? ByActivation { get; init; }
}

public record BreakdownItem
{
    public required string Dimension { get; init; }
    public required double Value { get; init; }
    public required long Count { get; init; }
}

public record TimeSeriesSummary
{
    public required double TotalValue { get; init; }
    public required long TotalCount { get; init; }
    public required double Average { get; init; }
    public required double Min { get; init; }
    public required double Max { get; init; }
    public required int DataPointCount { get; init; }
}

public record CategoryDiscoveryInfo
{
    public required string Category { get; init; }
    public required List<TypeDiscoveryInfo> Types { get; init; }
    public required int TotalMetrics { get; init; }
    public required long TotalRecords { get; init; }
}

public record TypeDiscoveryInfo
{
    public required string Type { get; init; }
    public required long SampleCount { get; init; }
    public required List<string> Units { get; init; }
    public required DateTime FirstSeen { get; init; }
    public required DateTime LastSeen { get; init; }
    public required List<string> Agents { get; init; }
    public required double SampleValue { get; init; }
}

public record CategoriesSummary
{
    public required int TotalCategories { get; init; }
    public required int TotalTypes { get; init; }
    public required long TotalRecords { get; init; }
    public required List<string> AvailableAgents { get; init; }
    public required DateRangeInfo DateRange { get; init; }
}
