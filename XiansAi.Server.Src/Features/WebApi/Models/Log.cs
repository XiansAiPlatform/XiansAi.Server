using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Features.WebApi.Models;

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
    public required string WorkflowRunId { get; set; }

    [BsonElement("properties")]
    public Dictionary<string, object>? Properties { get; set; }

    [BsonElement("exception")]
    public string? Exception { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

