using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Database.Models;

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

    [BsonElement("run_id")]
    public required string RunId { get; set; }

    [BsonElement("properties")]
    public Dictionary<string, object>? Properties { get; set; }

    [BsonElement("exception")]
    public string? Exception { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public enum LogLevel
{
    Information,
    Warning,
    Error,
    Debug,
    Critical
}
