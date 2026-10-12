namespace Shared.Data.Models.HealthChecks;

/// <summary>
/// Create/replace body for a health check. Exactly one of <see cref="Chat"/>, <see cref="Webhook"/>,
/// <see cref="Schedule"/> must be set, matching <see cref="Type"/>. Members are nullable so missing
/// values surface as validation messages rather than JSON binding failures.
/// </summary>
public class HealthCheckRequest
{
    public string? Name { get; set; }
    public HealthCheckType? Type { get; set; }
    public bool Enabled { get; set; } = true;
    public int? IntervalMinutes { get; set; }

    /// <summary>Chat and webhook checks only. Defaults to <see cref="HealthCheckLimits.DefaultTimeoutSeconds"/>.</summary>
    public int? TimeoutSeconds { get; set; }

    public ChatHealthCheckRequest? Chat { get; set; }
    public WebhookHealthCheckRequest? Webhook { get; set; }
    public ScheduleHealthCheckRequest? Schedule { get; set; }
}

public class HealthCheckAssertionRequest
{
    public HealthCheckAssertionOp? Op { get; set; }
    public string? Value { get; set; }
}

public class ChatHealthCheckRequest
{
    /// <summary>Flow name without the agent prefix. Omit to use the activation's conversational workflow.</summary>
    public string? WorkflowType { get; set; }
    public string? Message { get; set; }
    public List<HealthCheckAssertionRequest>? Assertions { get; set; }
}

public class WebhookHealthCheckRequest
{
    /// <summary>Flow name without the agent prefix.</summary>
    public string? WorkflowType { get; set; }

    /// <summary>The workflow's Temporal update method, as in <c>POST /api/user/webhooks/{workflow}/{methodName}</c>.</summary>
    public string? MethodName { get; set; }

    public Dictionary<string, string>? QueryParams { get; set; }
    public string? Body { get; set; }
    public int? ExpectedStatusCode { get; set; }
    public List<HealthCheckAssertionRequest>? Assertions { get; set; }
}

public class ScheduleHealthCheckRequest
{
    public string? ScheduleId { get; set; }
    public bool RequireLastRunSuccess { get; set; } = true;
}

public static class HealthCheckLimits
{
    public const int MaxNameLength = 100;
    public const int MinTimeoutSeconds = 5;
    public const int MaxTimeoutSeconds = 120;
    public const int DefaultTimeoutSeconds = 60;
    public const int MaxAssertions = 10;
    public const int MaxAssertionValueLength = 500;
    public const int MaxMessageLength = 4000;
    public const int MaxWorkflowTypeLength = 200;
    public const int MaxMethodNameLength = 200;
    public const int MaxQueryParams = 20;
    public const int MaxQueryParamKeyLength = 100;
    public const int MaxQueryParamValueLength = 1000;
    public const int MaxBodyLength = 32 * 1024;
    public const int MaxScheduleIdLength = 500;
    public const int MaxRunsPageSize = 100;
}

/// <summary>Overall health of an activation, derived from its checks' latest results.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ActivationHealthStatus>))]
public enum ActivationHealthStatus
{
    /// <summary>No enabled check has run yet.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unknown")] Unknown,

    /// <summary>Every enabled check that has run last passed.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("healthy")] Healthy,

    /// <summary>Some enabled checks last passed and some did not.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("degraded")] Degraded,

    /// <summary>No enabled check that has run last passed.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unhealthy")] Unhealthy
}

public sealed record HealthCheckStatusSummary(
    string CheckId,
    string Name,
    HealthCheckType Type,
    bool Enabled,
    HealthCheckStatus? LastStatus,
    DateTime? LastRunAt,
    string? LastDetail,
    int ConsecutiveFailures);

public sealed record ActivationHealthSummary(
    string ActivationId,
    string AgentName,
    string ActivationName,
    bool ActivationActive,
    ActivationHealthStatus Status,
    int TotalChecks,
    int EnabledChecks,
    int Passing,
    int Failing,
    int Erroring,
    DateTime? LastRunAt,
    List<HealthCheckStatusSummary> Checks);
