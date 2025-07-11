using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Features.WebApi.Models;

[BsonIgnoreExtraElements]
public class Activity : ModelValidatorBase
{
    // Regex patterns for validation
    private static readonly Regex ActivityIdPattern = new(@"^[a-zA-Z0-9._@-]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex ActivityNamePattern = new(@"^[a-zA-Z0-9\s._@-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex WorkflowIdPattern = new(@"^[a-zA-Z0-9._@-]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex WorkflowTypePattern = new(@"^[a-zA-Z0-9._-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex TaskQueuePattern = new(@"^[a-zA-Z0-9._-]{1,50}$", RegexOptions.Compiled);

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("activity_id")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Activity ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Activity ID contains invalid characters")]
    public string? ActivityId { get; set; }

    [BsonElement("activity_name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Activity name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]+$", ErrorMessage = "Activity name contains invalid characters")]
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
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Workflow ID contains invalid characters")]
    public string? WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Workflow type must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Workflow type contains invalid characters")]
    public string? WorkflowType { get; set; }

    [BsonElement("task_queue")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Task queue must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Task queue contains invalid characters")]
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

    public override void Sanitize()
    {
        // Sanitize string properties
        ActivityId = ValidationHelpers.SanitizeString(ActivityId);
        ActivityName = ValidationHelpers.SanitizeString(ActivityName);
        WorkflowId = ValidationHelpers.SanitizeString(WorkflowId);
        WorkflowType = ValidationHelpers.SanitizeString(WorkflowType);
        TaskQueue = ValidationHelpers.SanitizeString(TaskQueue);

        // Sanitize collections
        AgentToolNames = ValidationHelpers.SanitizeStringList(AgentToolNames);
        InstructionIds = ValidationHelpers.SanitizeStringList(InstructionIds);
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate activity ID if provided
        if (!string.IsNullOrEmpty(ActivityId))
        {
            if (!ValidationHelpers.IsValidPattern(ActivityId, ActivityIdPattern))
                throw new ValidationException("Invalid activity ID format");
        }

        // Validate activity name if provided
        if (!string.IsNullOrEmpty(ActivityName))
        {
            if (!ValidationHelpers.IsValidPattern(ActivityName, ActivityNamePattern))
                throw new ValidationException("Invalid activity name format");
        }

        // Validate workflow ID if provided
        if (!string.IsNullOrEmpty(WorkflowId))
        {
            if (!ValidationHelpers.IsValidPattern(WorkflowId, WorkflowIdPattern))
                throw new ValidationException("Invalid workflow ID format");
        }

        // Validate workflow type if provided
        if (!string.IsNullOrEmpty(WorkflowType))
        {
            if (!ValidationHelpers.IsValidPattern(WorkflowType, WorkflowTypePattern))
                throw new ValidationException("Invalid workflow type format");
        }

        // Validate task queue if provided
        if (!string.IsNullOrEmpty(TaskQueue))
        {
            if (!ValidationHelpers.IsValidPattern(TaskQueue, TaskQueuePattern))
                throw new ValidationException("Invalid task queue format");
        }

        // Validate dates
        if (StartedTime.HasValue && !ValidationHelpers.IsValidDate(StartedTime.Value))
            throw new ValidationException("Invalid started time");

        if (EndedTime.HasValue && !ValidationHelpers.IsValidDate(EndedTime.Value))
            throw new ValidationException("Invalid ended time");

        // Validate date range
        if (StartedTime.HasValue && EndedTime.HasValue)
        {
            if (!ValidationHelpers.IsValidDateRange(StartedTime.Value, EndedTime.Value))
                throw new ValidationException("Ended time must be after started time");
        }

        // Validate collections
        if (AgentToolNames != null)
        {
            if (!ValidationHelpers.IsValidList(AgentToolNames, item => !string.IsNullOrEmpty(item)))
                throw new ValidationException("Agent tool names list contains invalid items");
        }

        if (InstructionIds != null)
        {
            if (!ValidationHelpers.IsValidList(InstructionIds, item => !string.IsNullOrEmpty(item)))
                throw new ValidationException("Instruction IDs list contains invalid items");
        }
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
        if (!ValidationHelpers.IsValidPattern(sanitizedId, ActivityIdPattern))
            throw new ValidationException("Invalid activity ID format");

        return sanitizedId;
    }

    /// <summary>
    /// Validates and sanitizes an activity name
    /// </summary>
    /// <param name="activityName">The raw activity name to validate and sanitize</param>
    /// <returns>The sanitized activity name</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateActivityName(string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
            throw new ValidationException("Activity name is required");

        // First sanitize the activity name
        var sanitizedName = ValidationHelpers.SanitizeString(activityName);
        
        // Then validate the sanitized activity name format
        if (!ValidationHelpers.IsValidPattern(sanitizedName, ActivityNamePattern))
            throw new ValidationException("Invalid activity name format");

        return sanitizedName;
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
        if (!ValidationHelpers.IsValidPattern(sanitizedId, WorkflowIdPattern))
            throw new ValidationException("Invalid workflow ID format");

        return sanitizedId;
    }

    /// <summary>
    /// Validates and sanitizes a workflow type
    /// </summary>
    /// <param name="workflowType">The raw workflow type to validate and sanitize</param>
    /// <returns>The sanitized workflow type</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateWorkflowType(string workflowType)
    {
        if (string.IsNullOrWhiteSpace(workflowType))
            throw new ValidationException("Workflow type is required");

        // First sanitize the workflow type
        var sanitizedType = ValidationHelpers.SanitizeString(workflowType);
        
        // Then validate the sanitized workflow type format
        if (!ValidationHelpers.IsValidPattern(sanitizedType, WorkflowTypePattern))
            throw new ValidationException("Invalid workflow type format");

        return sanitizedType;
    }

    /// <summary>
    /// Validates and sanitizes a task queue
    /// </summary>
    /// <param name="taskQueue">The raw task queue to validate and sanitize</param>
    /// <returns>The sanitized task queue</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateTaskQueue(string taskQueue)
    {
        if (string.IsNullOrWhiteSpace(taskQueue))
            throw new ValidationException("Task queue is required");

        // First sanitize the task queue
        var sanitizedQueue = ValidationHelpers.SanitizeString(taskQueue);
        
        // Then validate the sanitized task queue format
        if (!ValidationHelpers.IsValidPattern(sanitizedQueue, TaskQueuePattern))
            throw new ValidationException("Invalid task queue format");

        return sanitizedQueue;
    }

    /// <summary>
    /// Validates and sanitizes activity data (ID, name, workflow ID, workflow type, task queue)
    /// </summary>
    /// <param name="activityId">The activity ID</param>
    /// <param name="activityName">The activity name</param>
    /// <param name="workflowId">The workflow ID</param>
    /// <param name="workflowType">The workflow type</param>
    /// <param name="taskQueue">The task queue</param>
    /// <returns>Tuple of sanitized values</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static (string activityId, string activityName, string workflowId, string workflowType, string taskQueue) 
        SanitizeAndValidateActivityData(string activityId, string activityName, string workflowId, string workflowType, string taskQueue)
    {
        var sanitizedActivityId = SanitizeAndValidateActivityId(activityId);
        var sanitizedActivityName = SanitizeAndValidateActivityName(activityName);
        var sanitizedWorkflowId = SanitizeAndValidateWorkflowId(workflowId);
        var sanitizedWorkflowType = SanitizeAndValidateWorkflowType(workflowType);
        var sanitizedTaskQueue = SanitizeAndValidateTaskQueue(taskQueue);

        return (sanitizedActivityId, sanitizedActivityName, sanitizedWorkflowId, sanitizedWorkflowType, sanitizedTaskQueue);
    }
}
