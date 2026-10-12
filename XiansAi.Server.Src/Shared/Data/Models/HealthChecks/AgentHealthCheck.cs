using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models.HealthChecks;

[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckType>))]
public enum HealthCheckType
{
    [JsonStringEnumMemberName("chat")] Chat,
    [JsonStringEnumMemberName("webhook")] Webhook,
    [JsonStringEnumMemberName("schedule")] Schedule
}

/// <summary>
/// Outcome of a single health-check run. <see cref="Fail"/> means the agent answered but the answer
/// was wrong; <see cref="Error"/> means no answer could be obtained (timeout, no worker, exception).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckStatus>))]
public enum HealthCheckStatus
{
    [JsonStringEnumMemberName("pass")] Pass,
    [JsonStringEnumMemberName("fail")] Fail,
    [JsonStringEnumMemberName("error")] Error
}

[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckAssertionOp>))]
public enum HealthCheckAssertionOp
{
    [JsonStringEnumMemberName("contains")] Contains,
    [JsonStringEnumMemberName("notContains")] NotContains
}

/// <summary>Whether the check's Temporal schedule is known to match its stored definition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckScheduleState>))]
public enum HealthCheckScheduleState
{
    /// <summary>Not yet created/updated in Temporal; the reconciler will retry.</summary>
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("paused")] Paused
}

/// <summary>Case-insensitive substring assertion (R5).</summary>
public class HealthCheckAssertion
{
    [BsonElement("op")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckAssertionOp Op { get; set; }

    [BsonElement("value")]
    public required string Value { get; set; }
}

public class ChatHealthCheckConfig
{
    /// <summary>Workflow to message. Null uses the activation's conversational workflow.</summary>
    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("message")]
    public required string Message { get; set; }

    [BsonElement("assertions")]
    public List<HealthCheckAssertion> Assertions { get; set; } = new();
}

/// <summary>Invokes the inbound webhook trigger path (Temporal update-with-start), per Q2.</summary>
public class WebhookHealthCheckConfig
{
    [BsonElement("workflow_type")]
    public required string WorkflowType { get; set; }

    [BsonElement("method_name")]
    public required string MethodName { get; set; }

    [BsonElement("query_params")]
    public Dictionary<string, string> QueryParams { get; set; } = new();

    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>When set, the WebhookResponse status code must equal this value.</summary>
    [BsonElement("expected_status_code")]
    public int? ExpectedStatusCode { get; set; }

    [BsonElement("assertions")]
    public List<HealthCheckAssertion> Assertions { get; set; } = new();
}

/// <summary>Metadata-only check of an existing agent schedule, per Q3.</summary>
public class ScheduleHealthCheckConfig
{
    [BsonElement("schedule_id")]
    public required string ScheduleId { get; set; }

    [BsonElement("require_last_run_success")]
    public bool RequireLastRunSuccess { get; set; } = true;
}

/// <summary>
/// A periodic health check attached to an <see cref="AgentActivation"/> (Q4). Exactly one of
/// <see cref="Chat"/>, <see cref="Webhook"/>, <see cref="Schedule"/> is set, matching <see cref="Type"/>.
/// Stored in the <c>agent_health_checks</c> collection.
/// </summary>
[BsonIgnoreExtraElements]
public class AgentHealthCheck
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("activation_id")]
    public required string ActivationId { get; set; }

    /// <summary>Denormalized from the activation at create time.</summary>
    [BsonElement("agent_name")]
    public required string AgentName { get; set; }

    /// <summary>Denormalized from the activation at create time; part of the target workflow id.</summary>
    [BsonElement("activation_name")]
    public required string ActivationName { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckType Type { get; set; }

    [BsonElement("enabled")]
    public bool Enabled { get; set; } = true;

    [BsonElement("interval_minutes")]
    public int IntervalMinutes { get; set; }

    [BsonElement("timeout_seconds")]
    public int TimeoutSeconds { get; set; }

    [BsonElement("chat")]
    [BsonIgnoreIfNull]
    public ChatHealthCheckConfig? Chat { get; set; }

    [BsonElement("webhook")]
    [BsonIgnoreIfNull]
    public WebhookHealthCheckConfig? Webhook { get; set; }

    [BsonElement("schedule")]
    [BsonIgnoreIfNull]
    public ScheduleHealthCheckConfig? Schedule { get; set; }

    // ----- Denormalized health, written after every run -----

    [BsonElement("last_run_at")]
    public DateTime? LastRunAt { get; set; }

    [BsonElement("last_status")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckStatus? LastStatus { get; set; }

    [BsonElement("last_detail")]
    public string? LastDetail { get; set; }

    [BsonElement("last_success_at")]
    public DateTime? LastSuccessAt { get; set; }

    [BsonElement("consecutive_failures")]
    public int ConsecutiveFailures { get; set; }

    [BsonElement("schedule_state")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckScheduleState ScheduleState { get; set; } = HealthCheckScheduleState.Pending;

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
