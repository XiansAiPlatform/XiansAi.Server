namespace Shared.Data.Models.Usage;

/// <summary>
/// Usage type enumeration - extensible for future metric types.
/// </summary>
public enum UsageType
{
    Tokens,
    Messages,
    ApiCalls,      // Future extension
    StorageBytes,  // Future extension
    ComputeTime    // Future extension
}

/// <summary>
/// Generic usage statistics response - works for any usage type.
/// </summary>
public record UsageStatisticsResponse
{
    public required string TenantId { get; init; }
    public string? UserId { get; init; }  // null = all users, value = specific user
    public required UsageType Type { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public required UsageMetrics TotalMetrics { get; init; }
    public required List<TimeSeriesDataPoint> TimeSeriesData { get; init; }
    public required List<UserBreakdown> UserBreakdown { get; init; }
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
/// User-level breakdown - generic for any usage type.
/// </summary>
public record UserBreakdown
{
    public required string UserId { get; init; }
    public string? UserName { get; init; }
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
/// Request parameters for usage statistics queries.
/// </summary>
public record UsageStatisticsRequest
{
    public required string TenantId { get; init; }
    public string? UserId { get; init; }  // null or "all" = all users
    public required UsageType Type { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public string GroupBy { get; init; } = "day";  // "day" | "week" | "month"
}

