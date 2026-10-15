namespace Shared.Data.Models.HealthChecks;

/// <summary>
/// The request body to create or replace a health check. Set only one of Chat, Webhook or Schedule,
/// based on Type. Fields can be null so that a missing value gives a clear validation message.
/// </summary>
public class HealthCheckRequest
{
    public string? Name { get; set; }
    public HealthCheckType? Type { get; set; }
    public bool Enabled { get; set; } = true;
    public int? IntervalMinutes { get; set; }

    /// <summary>Chat and webhook checks only. Defaults to 60 seconds.</summary>
    public int? TimeoutSeconds { get; set; }

    public ChatHealthCheckRequest? Chat { get; set; }
    public WebhookHealthCheckRequest? Webhook { get; set; }
    public ScheduleHealthCheckRequest? Schedule { get; set; }

    /// <summary>
    /// Ids of the tenant contacts to email when the check starts failing and when it passes again.
    /// Leave it out or send an empty list to email no one.
    /// </summary>
    public List<string>? NotifyContactIds { get; set; }
}

public class HealthCheckAssertionRequest
{
    public HealthCheckAssertionOp? Op { get; set; }

    /// <summary>The JSON path (like <c>$.data.status</c>) or the header name.</summary>
    public string? Path { get; set; }

    public string? Value { get; set; }
}

public class ChatHealthCheckRequest
{
    /// <summary>The flow name, without the agent name. Leave it out to use the activation's chat workflow.</summary>
    public string? WorkflowType { get; set; }
    public string? Message { get; set; }
    public List<HealthCheckAssertionRequest>? Assertions { get; set; }
}

public class WebhookHealthCheckRequest
{
    /// <summary><c>update</c> (default) or <c>builtin</c>.</summary>
    public WebhookHealthCheckMode? Mode { get; set; }

    /// <summary>The flow name, without the agent name.</summary>
    public string? WorkflowType { get; set; }

    /// <summary>Update mode: the update method name.</summary>
    public string? MethodName { get; set; }

    /// <summary>Built-in mode: the webhook name.</summary>
    public string? WebhookName { get; set; }

    /// <summary>Optional. The check fails if the agent takes longer than this to answer.</summary>
    public int? MaxResponseMs { get; set; }

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
    public const int MaxAssertionPathLength = 200;
    public const int MaxWebhookNameLength = 200;
    public const int MaxMessageLength = 4000;
    public const int MaxWorkflowTypeLength = 200;
    public const int MaxMethodNameLength = 200;
    public const int MaxQueryParams = 20;
    public const int MaxQueryParamKeyLength = 100;
    public const int MaxQueryParamValueLength = 1000;
    public const int MaxBodyLength = 32 * 1024;
    public const int MaxScheduleIdLength = 500;
    public const int MaxRunsPageSize = 100;
    public const int MaxNotifyContacts = 10;
}

/// <summary>The overall health of an activation, based on the latest result of each check.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ActivationHealthStatus>))]
public enum ActivationHealthStatus
{
    /// <summary>No enabled check has run yet.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("unknown")] Unknown,

    /// <summary>All checks that have run passed last time.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("healthy")] Healthy,

    /// <summary>Some checks passed last time and some did not.</summary>
    [System.Text.Json.Serialization.JsonStringEnumMemberName("degraded")] Degraded,

    /// <summary>None of the checks that have run passed last time.</summary>
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
