using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models.HealthChecks;

public class HealthCheckAssertionResult
{
    [BsonElement("op")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckAssertionOp Op { get; set; }

    [BsonElement("path")]
    [BsonIgnoreIfNull]
    public string? Path { get; set; }

    [BsonElement("value")]
    public string Value { get; set; } = string.Empty;

    [BsonElement("passed")]
    public bool Passed { get; set; }

    /// <summary>The value found in the response, for JSON and header checks (shortened).</summary>
    [BsonElement("actual")]
    [BsonIgnoreIfNull]
    public string? Actual { get; set; }
}

/// <summary>
/// The saved result of one run, in the <c>agent_health_check_runs</c> collection.
/// Deleted after 30 days by default.
/// </summary>
[BsonIgnoreExtraElements]
public class AgentHealthCheckRun
{
    public const int MaxDetailLength = 1000;
    public const int MaxSnippetLength = 500;

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("check_id")]
    public required string CheckId { get; set; }

    [BsonElement("activation_id")]
    public required string ActivationId { get; set; }

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckType Type { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public HealthCheckStatus Status { get; set; }

    [BsonElement("started_at")]
    public DateTime StartedAt { get; set; }

    [BsonElement("duration_ms")]
    public long DurationMs { get; set; }

    [BsonElement("detail")]
    public string? Detail { get; set; }

    [BsonElement("response_snippet")]
    [BsonIgnoreIfNull]
    public string? ResponseSnippet { get; set; }

    [BsonElement("assertion_results")]
    public List<HealthCheckAssertionResult> AssertionResults { get; set; } = new();

    /// <summary>The Temporal run id of the workflow that made this result.</summary>
    [BsonElement("workflow_run_id")]
    [BsonIgnoreIfNull]
    public string? WorkflowRunId { get; set; }
}
