using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Features.AgentApi.Models;

[BsonIgnoreExtraElements]
public class Log
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("level")]
    [BsonRepresentation(BsonType.String)]
    public required LogLevel Level { get; set; }

    [BsonElement("message")]
    public required string Message { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    [BsonElement("workflow_run_id")]
    public string? WorkflowRunId { get; set; }

    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("agent")]
    public required string Agent { get; set; }

    [BsonElement("activation")]
    public string? Activation { get; set; }

    [BsonElement("participant_id")]
    public string? ParticipantId { get; set; }

    [BsonElement("properties")]
    public Dictionary<string, object>? Properties { get; set; }

    [BsonElement("exception")]
    public string? Exception { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

