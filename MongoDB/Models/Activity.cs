using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.MongoDB.Models;

public class Activity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("activity_id")]
    public string? ActivityId { get; set; }

    [BsonElement("activity_name")]
    public string? ActivityName { get; set; }

    [BsonElement("started_time")]
    public DateTime? StartedTime { get; set; }

    [BsonElement("ended_time")]
    public DateTime? EndedTime { get; set; }

    [BsonElement("inputs")]
    public Dictionary<string, object?>? Inputs { get; set; } = [];

    [BsonElement("result")]
    public object? Result { get; set; }

    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("task_queue")]
    public string? TaskQueue { get; set; }

    [BsonElement("agent_name")]
    public string? AgentName { get; set; }

    [BsonElement("instruction_ids")]
    public List<string>? InstructionIds { get; set; } = [];
}
