using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class ActivityDefinition
{
    private List<string>? _agentToolNames;

    [BsonElement("activity_name")]
    public required string ActivityName { get; set; }

    [Obsolete("Maintained for backward compatibility only. Use AgentToolNames instead.")]
    [BsonElement("agent_names")]
    public List<string>? AgentNames {
        get {
            return _agentToolNames;
        }
        set {
            _agentToolNames = value;
        }
    }
    [BsonElement("agent_tool_names")]
    public List<string>? AgentToolNames {
        get {
            return _agentToolNames;
        }
        set {
            _agentToolNames = value;
        }
    }

    [BsonElement("instructions")]
    public required List<string> Instructions { get; set; }

    [BsonElement("parameters")]
    public required List<ParameterDefinition> Parameters { get; set; }
}