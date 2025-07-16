using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;


namespace Features.WebApi.Models;

public class Activity : ModelValidatorBase<Activity>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("activity_id")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Activity ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[0-9]+$", ErrorMessage = "Activity ID contains invalid characters")]
    public string? ActivityId { get; set; }

    [BsonElement("activity_name")]
     [StringLength(100, MinimumLength = 1, ErrorMessage = "Activity name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Activity name contains invalid characters")]
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
     [StringLength(50, MinimumLength = 1, ErrorMessage = "Workflow ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Workflow ID contains invalid characters")]
    public string? WorkflowId { get; set; }

    [BsonElement("workflow_type")]
     [StringLength(100, MinimumLength = 1, ErrorMessage = "Workflow type must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Workflow type contains invalid characters")]
    public string? WorkflowType { get; set; }

    [BsonElement("task_queue")]
 [StringLength(50, MinimumLength = 1, ErrorMessage = "Task queue must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Task queue contains invalid characters")]
        public string? TaskQueue { get; set; }

    [Obsolete("Maintained for backward compatibility only. Use AgentToolNames instead.")]
    [BsonElement("agent_name")]
    public List<string>? AgentNames {
        get {
            return AgentToolNames;
        }
        set {
            AgentToolNames = value;
        }
    }

    [BsonElement("agent_tool_names")]
    public List<string>? AgentToolNames { get; set; } = [];

    [BsonElement("instruction_ids")]
    public List<string>? InstructionIds { get; set; } = [];
    public override Activity SanitizeAndReturn()
    {
        // Create a new activity with sanitized data
        var sanitizedActivity = new Activity
        {
            Id = this.Id,
            ActivityId = ValidationHelpers.SanitizeString(this.ActivityId),
            ActivityName = ValidationHelpers.SanitizeString(this.ActivityName),
            StartedTime = this.StartedTime,
            EndedTime = this.EndedTime,
            Inputs = this.Inputs,
            Result = this.Result,
            WorkflowId = ValidationHelpers.SanitizeString(this.WorkflowId),
            WorkflowType = ValidationHelpers.SanitizeString(this.WorkflowType),
            TaskQueue = ValidationHelpers.SanitizeString(this.TaskQueue),
            AgentToolNames = ValidationHelpers.SanitizeStringList(this.AgentToolNames),
            InstructionIds = ValidationHelpers.SanitizeStringList(this.InstructionIds)
        };

        return sanitizedActivity;
    }

    public override Activity SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedActivity = this.SanitizeAndReturn();
        sanitizedActivity.Validate();

        return sanitizedActivity;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();
    }
    /// <summary>
    /// Validates and sanitizes an activity ID
    /// </summary>
    /// <param name="activityId">The raw activity ID to validate and sanitize</param>
    /// <returns>The sanitized activity ID</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateActivityId(string activityId)
    {
        if (string.IsNullOrWhiteSpace(activityId))
            throw new ValidationException("Activity ID is required");

        // First sanitize the activity ID
        var sanitizedId = ValidationHelpers.SanitizeString(activityId);

        // Then validate the sanitized activity ID format
        if (!ValidationHelpers.IsValidPattern(sanitizedId, ValidationHelpers.Patterns.ActivityIdPattern))
            throw new ValidationException("Invalid activity ID format");

        return sanitizedId;
    }
    /// <summary>
    /// Validates and sanitizes a workflow ID
    /// </summary>
    /// <param name="workflowId">The raw workflow ID to validate and sanitize</param>
    /// <returns>The sanitized workflow ID</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateWorkflowId(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ValidationException("Workflow ID is required");

        // First sanitize the workflow ID
        var sanitizedId = ValidationHelpers.SanitizeString(workflowId);

        // Then validate the sanitized workflow ID format
        if (!ValidationHelpers.IsValidPattern(sanitizedId, ValidationHelpers.Patterns.WorkflowIdPattern))
            throw new ValidationException("Invalid workflow ID format");

        return sanitizedId;
    }
}
