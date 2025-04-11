using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Features.AgentApi.Repositories;
using Shared.Data.Models;

namespace Features.AgentApi.Services.Lib;
public class ActivityHistoryRequest
{
    [Required]
    [JsonPropertyName("activityId")]
    public required string ActivityId { get; set; }

    [Required]
    [JsonPropertyName("activityName")]
    public required string ActivityName { get; set; }

    [JsonPropertyName("startedTime")]
    public DateTime? StartedTime { get; set; }

    [JsonPropertyName("endedTime")]
    public DateTime? EndedTime { get; set; }

    [Required]
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; set; }

    [Required]
    [JsonPropertyName("workflowRunId")]
    public required string WorkflowRunId { get; set; }

    [Required]
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    [Required]
    [JsonPropertyName("taskQueue")]
    public required string TaskQueue { get; set; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement?>? Inputs { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("agentToolNames")]
    public List<string>? AgentToolNames { get; set; }

    [JsonPropertyName("instructionIds")]
    public List<string>? InstructionIds { get; set; } = [];

    [JsonPropertyName("attempt")]
    public int Attempt { get; set; }

    [Required]
    [JsonPropertyName("workflowNamespace")]
    public required string WorkflowNamespace { get; set; }
}


public class ActivityHistoryService
{
    private readonly ILogger<ActivityHistoryService> _logger;
    private readonly ActivityHistoryRepository _activityHistoryRepository;

    public ActivityHistoryService(
        ActivityHistoryRepository activityHistoryRepository,
        ILogger<ActivityHistoryService> logger
    )
    {
        _activityHistoryRepository = activityHistoryRepository;
        _logger = logger;
    }

    public void Create(ActivityHistoryRequest request)
    {
        _logger.LogInformation("Received request to create activity at: {Time}: {Request}", 
            DateTime.UtcNow, JsonSerializer.Serialize(request));
        var activity = new ActivityHistory
        {
            Id = Guid.NewGuid(),
            ActivityId = request.ActivityId,
            ActivityName = request.ActivityName,
            StartedTime = request.StartedTime,
            EndedTime = request.EndedTime,
            WorkflowId = request.WorkflowId,
            WorkflowRunId = request.WorkflowRunId,
            WorkflowType = request.WorkflowType,
            TaskQueue = request.TaskQueue,
            Inputs = ConvertJsonElementToBsonCompatible(request.Inputs),
            Result = ConvertJsonElementToBsonCompatible(request.Result),
            AgentToolNames = request.AgentToolNames,
            InstructionIds = request.InstructionIds ?? new List<string>(),
            Attempt = request.Attempt,
            WorkflowNamespace = request.WorkflowNamespace
        };

        // Fire and forget
        _activityHistoryRepository.CreateWithoutWaiting(activity);
    }


    private Dictionary<string, object?> ConvertJsonElementToBsonCompatible(Dictionary<string, JsonElement?>? elements)
    {
        if (elements == null) return new Dictionary<string, object?>();
        
        return elements.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertJsonElementToBsonCompatible(kvp.Value)
        );
    }

    private object? ConvertJsonElementToBsonCompatible(JsonElement? element)
    {
        if (element == null) return null;

        switch (element.Value.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Number:
                if (element.Value.TryGetInt64(out long l))
                    return l;
                return element.Value.GetDouble();
            case JsonValueKind.String:
                return element.Value.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Array:
                return element.Value.EnumerateArray()
                    .Select(e => ConvertJsonElementToBsonCompatible(e))
                    .ToList();
            case JsonValueKind.Object:
                return element.Value.EnumerateObject()
                    .ToDictionary(
                        p => p.Name,
                        p => ConvertJsonElementToBsonCompatible(p.Value)
                    );
            default:
                return null;
        }
    }
}
