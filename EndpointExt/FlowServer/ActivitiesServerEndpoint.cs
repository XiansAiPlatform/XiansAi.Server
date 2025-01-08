using System.Text.Json;
using MongoDB.Bson;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using XiansAi.Server.MongoDB.Repositories;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB;

namespace XiansAi.Server.EndpointExt.FlowServer;
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

    [Required]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [JsonPropertyName("instructionIds")]
    public List<string>? InstructionIds { get; set; } = [];
}

public class ActivitiesServerEndpoint
{
    private readonly ILogger<ActivitiesServerEndpoint> _logger;
    private readonly IDatabaseService _databaseService;


    public ActivitiesServerEndpoint(
        IDatabaseService databaseService,
        ILogger<ActivitiesServerEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task CreateAsync(ActivityRequest request)
    {
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
            AgentName = request.AgentName,
            InstructionIds = request.InstructionIds ?? new List<string>()
        };
        var activityRepository = new ActivityRepository(await _databaseService.GetDatabase());
        await activityRepository.CreateAsync(activity);
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
