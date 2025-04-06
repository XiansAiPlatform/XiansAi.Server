using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;
public class FlowDefinition
{
    private string _agentName = string.Empty;

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("type_name")]
    public required string TypeName { get; set; }

    [BsonElement("agent_name")]
    public required string AgentName { 
        get
        {
            if (string.IsNullOrEmpty(_agentName))
            {
                _agentName = TypeName;
            }
            return _agentName;
        }
        set
        {
            _agentName = value;
        }
    }

    [BsonElement("class_name")]
    public string? ClassName { get; set; }

    [BsonElement("hash")]
    public required string Hash { get; set; }

    [BsonElement("source")]
    public string? Source { get; set; } = string.Empty;

    [BsonElement("markdown")]
    public string? Markdown { get; set; } = string.Empty;

    [BsonElement("activities")]
    public required List<ActivityDefinition> Activities { get; set; }

    [BsonElement("parameters")]
    public required List<ParameterDefinition> Parameters { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("owner")]
    public required string Owner { get; set; }

    [BsonElement("permissions")]
    public List<PermissionDefinition>? Permissions { get; set; }
}

public class PermissionDefinition
{
    [BsonElement("level")]
    public string? Level { get; set; }

    [BsonElement("owner")]
    public string? Owner { get; set; }
}