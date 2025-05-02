using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public class ActivityDefinition
{

    [BsonElement("activity_name")]
    public required string ActivityName { get; set; }

    [BsonElement("agent_tool_names")]
    public List<string>? AgentToolNames { get; set; }

    [BsonElement("knowledge_ids")]
    public required List<string> KnowledgeIds { get; set; }

    [BsonElement("parameter_definitions")]
    public required List<ParameterDefinition> ParameterDefinitions { get; set; }
}