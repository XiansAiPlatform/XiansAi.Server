using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Features.AgentApi.Data.Repositories;
using Features.AgentApi.Data.Models;

namespace Features.AgentApi.Services.Lib;
public class ActivityRequest
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
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    [Required]
    [JsonPropertyName("taskQueue")]
    public required string TaskQueue { get; set; }

    [JsonPropertyName("inputs")]
    public Dictionary<string, JsonElement?>? Inputs { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [Obsolete("Maintained for backward compatibility only. Use AgentToolNames instead.")]
    [JsonPropertyName("agentNames")]
    public List<string>? AgentNames {
        get {
            return AgentToolNames;
        }
        set {
            AgentToolNames = value;
        }
    }

    [JsonPropertyName("agentToolNames")]
    public List<string>? AgentToolNames { get; set; }

    [JsonPropertyName("instructionIds")]
    public List<string>? InstructionIds { get; set; } = [];
}

public class ActivityEndTimeRequest
{
    [Required]
    [JsonPropertyName("activityId")]
    public required string ActivityId { get; set; }

    [Required]
    [JsonPropertyName("endTime")]
    public required DateTime EndTime { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }
}

public class ActivityHistoryService
{
    private readonly ILogger<ActivityHistoryService> _logger;
    private readonly ActivityRepository _activityRepository;

    public ActivityHistoryService(
        ActivityRepository activityRepository,
        ILogger<ActivityHistoryService> logger
    )
    {
        _activityRepository = activityRepository;
        _logger = logger;
    }

    public async Task CreateAsync(ActivityRequest request)
    {
        _logger.LogInformation("Received request to create activity at: {Time}: {Request}", 
            DateTime.UtcNow, JsonSerializer.Serialize(request));
        var activity = new Activity
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ActivityId = request.ActivityId,
            ActivityName = request.ActivityName,
            StartedTime = request.StartedTime,
            EndedTime = request.EndedTime,
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            TaskQueue = request.TaskQueue,
            Inputs = ConvertJsonElementToBsonCompatible(request.Inputs),
            Result = ConvertJsonElementToBsonCompatible(request.Result),
            AgentToolNames = request.AgentToolNames,
            InstructionIds = request.InstructionIds ?? new List<string>()
        };
        await _activityRepository.CreateAsync(activity);
    }

    public async Task<IResult> UpdateEndTimeAsync(ActivityEndTimeRequest request)
    {
        _logger.LogInformation("Updating end time for activity {ActivityId} to {EndTime}", 
            request.ActivityId, request.EndTime);
        
        var result = ConvertJsonElementToBsonCompatible(request.Result);
        if (result == null)
        {
            return Results.BadRequest(new { message = "Result is required" });
        }
        var success = await _activityRepository.UpdateEndTimeAsync(request.ActivityId, request.EndTime, result);
        
        if (success)
        {
            return Results.Ok(new { message = "Activity end time updated successfully" });
        }
        else
        {
            return Results.NotFound(new { message = $"Activity with ID {request.ActivityId} not found" });
        }
    }

    public async Task<IResult> GetByWorkflowIdAsync(string workflowId)
    {
        _logger.LogInformation("Getting activities for workflow {WorkflowId}", workflowId);
        var activities = await _activityRepository.GetByWorkflowIdAsync(workflowId);
        return Results.Ok(activities);
    }

    public async Task<IResult> GetActivityAsync(string workflowId, string activityId)
    {
        _logger.LogInformation("Getting activity {ActivityId} for workflow {WorkflowId}", activityId, workflowId);
        var activity = await _activityRepository.GetByWorkflowIdAndActivityIdAsync(workflowId, activityId);
        
        if (activity == null)
        {
            return Results.NotFound(new { message = $"Activity {activityId} in workflow {workflowId} not found" });
        }
        
        return Results.Ok(activity);
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
