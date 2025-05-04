using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class FlowDefinition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("workflow_type")]
    public required string WorkflowType { get; set; }

    [BsonElement("agent")]
    public required string Agent { get; set; }

    [BsonElement("knowledge_ids")]
    public required List<string> KnowledgeIds { get; set; }

    [BsonElement("hash")]
    public required string Hash { get; set; }

    [BsonElement("source")]
    public string? Source { get; set; } = string.Empty;

    [BsonElement("markdown")]
    public string? Markdown { get; set; } = string.Empty;

    [BsonElement("activities")]
    public required List<ActivityDefinition> ActivityDefinitions { get; set; }

    [BsonElement("parameters")]
    public required List<ParameterDefinition> ParameterDefinitions { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("permissions")]
    public required Permission Permissions { get; set; }
}