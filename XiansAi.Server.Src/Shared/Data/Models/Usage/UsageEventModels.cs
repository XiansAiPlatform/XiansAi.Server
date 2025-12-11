using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Usage;

/// <summary>
/// Usage event model - represents a single usage event (tokens, messages, etc.) stored in MongoDB.
/// </summary>
[BsonIgnoreExtraElements]
public class UsageEvent
{
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

    [BsonElement("message_count")]
    public long MessageCount { get; set; }

    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }

    [BsonElement("request_id")]
    public string? RequestId { get; set; }

    [BsonElement("source")]
    public string? Source { get; set; }

    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [BsonElement("response_time_ms")]
    public long? ResponseTimeMs { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Record for usage event tracking - used for creating usage events.
/// </summary>
public record UsageEventRecord(
    string TenantId,
    string UserId,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    long MessageCount,
    string? WorkflowId,
    string? RequestId,
    string? Source,
    Dictionary<string, string>? Metadata,
    long? ResponseTimeMs = null);

/// <summary>
/// Usage type enumeration - extensible for future metric types.
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
    public string? UserId { get; init; }  // null = all users, value = specific user
    public required UsageType Type { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
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
    public required string UserId { get; init; }
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
    public required string UserId { get; init; }
    public string? UserName { get; init; }
    public string? Email { get; init; }
}

/// <summary>
/// Request parameters for usage events queries.
/// </summary>
public record UsageEventsRequest
{
    public required string TenantId { get; init; }
    public string? UserId { get; init; }  // null or "all" = all users
    public string? AgentName { get; init; }  // null or "all" = all agents
    public required UsageType Type { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public string GroupBy { get; init; } = "day";  // "day" | "week" | "month"
}

/// <summary>
/// Request model for usage report endpoint.
/// </summary>
public class UsageReportRequest
{
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? Model { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public long MessageCount { get; set; }
    public string? WorkflowId { get; set; }
    public string? RequestId { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public long? ResponseTimeMs { get; set; }
}

