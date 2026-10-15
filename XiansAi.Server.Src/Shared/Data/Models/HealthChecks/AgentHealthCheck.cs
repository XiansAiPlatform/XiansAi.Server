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
/// The result of one run. Fail: the agent answered, but the answer was wrong.
/// Error: the agent did not answer (timeout, no worker, or an exception).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckStatus>))]
public enum HealthCheckStatus
{
    [JsonStringEnumMemberName("pass")] Pass,
    [JsonStringEnumMemberName("fail")] Fail,
    [JsonStringEnumMemberName("error")] Error
}

/// <summary>
/// What a check looks at. Text checks look at the whole response. JSON checks look at one value
/// (for example <c>$.data.status</c>). Header checks look at one response header.
/// Chat checks can only use the text checks.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckAssertionOp>))]
public enum HealthCheckAssertionOp
{
    [JsonStringEnumMemberName("contains")] Contains,
    [JsonStringEnumMemberName("notContains")] NotContains,
    [JsonStringEnumMemberName("jsonEquals")] JsonEquals,
    [JsonStringEnumMemberName("jsonExists")] JsonExists,
    [JsonStringEnumMemberName("headerEquals")] HeaderEquals,
    [JsonStringEnumMemberName("headerExists")] HeaderExists
}

/// <summary>How a webhook check calls the agent.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WebhookHealthCheckMode>))]
public enum WebhookHealthCheckMode
{
    /// <summary>Calls an update method on the workflow, like <c>/api/user/webhooks/{workflow}/{method}</c>.</summary>
    [JsonStringEnumMemberName("update")] Update,

    /// <summary>Calls the agent's <c>OnWebhook</c> handler, like <c>/api/user/webhooks/builtin</c>.</summary>
    [JsonStringEnumMemberName("builtin")] Builtin
}

/// <summary>Whether the check's Temporal schedule is up to date.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthCheckScheduleState>))]
public enum HealthCheckScheduleState
{
    /// <summary>Not saved in Temporal yet. The reconciler will try again.</summary>
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("active")] Active,
    [JsonStringEnumMemberName("paused")] Paused
}

/// <summary>
/// One check on a response: some text, a JSON value, or a header.
/// </summary>
public class HealthCheckAssertion
{
    [BsonElement("op")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckAssertionOp Op { get; set; }

    /// <summary>The JSON path or the header name. Not used by text checks.</summary>
    [BsonElement("path")]
    [BsonIgnoreIfNull]
    public string? Path { get; set; }

    /// <summary>The expected text or value. Not used by the "exists" checks.</summary>
    [BsonElement("value")]
    public string Value { get; set; } = string.Empty;
}

public class ChatHealthCheckConfig
{
    /// <summary>The workflow to send the message to. If empty, the activation's chat workflow is used.</summary>
    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("message")]
    public required string Message { get; set; }

    [BsonElement("assertions")]
    public List<HealthCheckAssertion> Assertions { get; set; } = new();
}

/// <summary>
/// Calls one of the agent's webhooks: an update method or the <c>OnWebhook</c> handler.
/// Old checks saved without a mode are treated as update mode.
/// </summary>
public class WebhookHealthCheckConfig
{
    [BsonElement("mode")]
    [BsonRepresentation(BsonType.String)]
    public WebhookHealthCheckMode Mode { get; set; } = WebhookHealthCheckMode.Update;

    [BsonElement("workflow_type")]
    public required string WorkflowType { get; set; }

    /// <summary>Update mode: the update method name.</summary>
    [BsonElement("method_name")]
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Built-in mode: the webhook name sent to the agent's <c>OnWebhook</c> handler.</summary>
    [BsonElement("webhook_name")]
    [BsonIgnoreIfNull]
    public string? WebhookName { get; set; }

    /// <summary>If set, the check fails when the response takes longer than this.</summary>
    [BsonElement("max_response_ms")]
    [BsonIgnoreIfNull]
    public int? MaxResponseMs { get; set; }

    [BsonElement("query_params")]
    public Dictionary<string, string> QueryParams { get; set; } = new();

    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>If set, the response status code must be this value.</summary>
    [BsonElement("expected_status_code")]
    public int? ExpectedStatusCode { get; set; }

    [BsonElement("assertions")]
    public List<HealthCheckAssertion> Assertions { get; set; } = new();
}

/// <summary>Checks the state of an agent schedule. It never runs the schedule.</summary>
public class ScheduleHealthCheckConfig
{
    [BsonElement("schedule_id")]
    public required string ScheduleId { get; set; }

    [BsonElement("require_last_run_success")]
    public bool RequireLastRunSuccess { get; set; } = true;
}

/// <summary>
/// A health check that runs on a schedule for one activation.
/// Only one of Chat, Webhook or Schedule is set, based on Type.
/// Saved in the <c>agent_health_checks</c> collection.
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

    /// <summary>Copied from the activation when the check is created.</summary>
    [BsonElement("agent_name")]
    public required string AgentName { get; set; }

    /// <summary>Copied from the activation when the check is created. Used to build the workflow id.</summary>
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

    /// <summary>
    /// Ids of the tenant contacts to email when the check starts failing and when it passes again.
    /// Deleted contacts are skipped.
    /// </summary>
    [BsonElement("notify_contact_ids")]
    public List<string> NotifyContactIds { get; set; } = new();

    // ----- Latest status, updated after every run -----

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
