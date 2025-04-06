using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public class ActivityHistory
{
    [JsonPropertyName("id")]
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [JsonPropertyName("activityId")]
    [BsonElement("activity_id")]
    public string? ActivityId { get; set; }

    [JsonPropertyName("activityName")]
    [BsonElement("activity_name")]
    public string? ActivityName { get; set; }

    [JsonPropertyName("startedTime")]
    [BsonElement("started_time")]
    public DateTime? StartedTime { get; set; }

    [JsonPropertyName("endedTime")]
    [BsonElement("ended_time")]
    public DateTime? EndedTime { get; set; }

    [JsonPropertyName("inputs")]
    [BsonElement("inputs")]
    public Dictionary<string, object?>? Inputs { get; set; } = [];

    [JsonPropertyName("result")]
    [BsonElement("result")]
    public object? Result { get; set; }

    [JsonPropertyName("workflowId")]
    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }

    [JsonPropertyName("workflowRunId")]
    [BsonElement("workflow_run_id")]
    public string? WorkflowRunId { get; set; }

    [JsonPropertyName("workflowType")]
    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [JsonPropertyName("taskQueue")]
    [BsonElement("task_queue")]
    public string? TaskQueue { get; set; }

    [JsonPropertyName("agentToolNames")]
    [BsonElement("agent_tool_names")]
    public List<string>? AgentToolNames { get; set; } = [];

    [JsonPropertyName("instructionIds")]
    [BsonElement("instruction_ids")]
    public List<string>? InstructionIds { get; set; } = [];

    [JsonPropertyName("attempt")]
    [BsonElement("attempt")]
    public int Attempt { get; set; }

    [JsonPropertyName("workflowNamespace")]
    [BsonElement("workflow_namespace")]
    public required string WorkflowNamespace { get; set; }
} 