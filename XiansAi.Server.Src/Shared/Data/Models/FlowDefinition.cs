using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public partial class FlowDefinition : ModelValidatorBase<FlowDefinition>
{
[BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "Flow definition ID is required")]
    public required string Id { get; set; }

    [BsonElement("workflow_type")]
    [Required(ErrorMessage = "Workflow type is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Workflow type must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#='’]+$", ErrorMessage = "Workflow type contains invalid characters")]
    public required string WorkflowType { get; set; }

    [BsonElement("agent")]
    [Required(ErrorMessage = "Agent is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Agent must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Agent contains invalid characters")]
    public required string Agent { get; set; }

    [BsonElement("hash")]
    [Required(ErrorMessage = "Hash is required")]
    [StringLength(64, MinimumLength = 1, ErrorMessage = "Hash must be between 1 and 64 characters")]
    [RegularExpression(@"^[a-fA-F0-9]+$", ErrorMessage = "Hash contains invalid characters")]
    public required string Hash { get; set; }

    [BsonElement("source")]
    [StringLength(10000, ErrorMessage = "Source cannot exceed 10000 characters")]
    public string? Source { get; set; } = string.Empty;

    [BsonElement("markdown")]
    [StringLength(50000, ErrorMessage = "Markdown cannot exceed 50000 characters")]
    public string? Markdown { get; set; } = string.Empty;

    [BsonElement("activities")]
    [Required(ErrorMessage = "Activity definitions are required")]
    public required List<ActivityDefinition> ActivityDefinitions { get; set; }

    [BsonElement("parameters")]
    [Required(ErrorMessage = "Parameter definitions are required")]
    public required List<ParameterDefinition> ParameterDefinitions { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("created_by")]
    [Required(ErrorMessage = "Created by is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    public required string CreatedBy { get; set; }

     public override FlowDefinition SanitizeAndReturn()
    {
        // Create a new flow definition with sanitized data
        var sanitizedFlowDefinition = new FlowDefinition
        {
            Id = ValidationHelpers.SanitizeString(Id),
            WorkflowType = ValidationHelpers.SanitizeString(WorkflowType),
            Agent = ValidationHelpers.SanitizeString(Agent),
            Hash = ValidationHelpers.SanitizeString(Hash),
            Source = ValidationHelpers.SanitizeString(Source),
            Markdown = ValidationHelpers.SanitizeString(Markdown),
            CreatedBy = ValidationHelpers.SanitizeString(CreatedBy),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            ActivityDefinitions = ActivityDefinitions?.Select(a =>
            {
                if (a is IModelValidator<ActivityDefinition> validator)
                {
                    return validator.SanitizeAndReturn();
                }
                return a;
            }).ToList() ?? new List<ActivityDefinition>(),
            ParameterDefinitions = ParameterDefinitions?.Select(p =>
            {
                if (p is IModelValidator<ParameterDefinition> validator)
                {
                    return validator.SanitizeAndReturn();
                }
                return p;
            }).ToList() ?? new List<ParameterDefinition>()
        };

        return sanitizedFlowDefinition;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Additional custom validation  
        if (!ValidationHelpers.IsValidDate(CreatedAt))
            throw new ValidationException("Invalid creation date");

        if (!ValidationHelpers.IsValidDate(UpdatedAt))
            throw new ValidationException("Invalid update date");

        if (!ValidationHelpers.IsValidDateRange(CreatedAt, UpdatedAt))
            throw new ValidationException("Updated date cannot be before created date");

        // Validate collections
        if (!ValidationHelpers.IsValidList(ActivityDefinitions, a => a != null))
            throw new ValidationException("Activity definitions cannot be null");

        if (!ValidationHelpers.IsValidList(ParameterDefinitions, p => p != null))
            throw new ValidationException("Parameter definitions cannot be null");
    }

    /// <summary>
    /// Validates and sanitizes a workflow type
    /// </summary>
    /// <param name="workflowType">The raw workflow type to validate and sanitize</param>
    /// <returns>The sanitized workflow type</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateType(string workflowType)
    {      

        // Sanitize the workflow type
        var sanitizedType = ValidationHelpers.SanitizeString(workflowType);

        // Validate the workflow type format using the same pattern as the WorkflowType property
        if (!ValidationHelpers.IsValidPattern(sanitizedType, ValidationHelpers.Patterns.WorkflowTypePattern))
            throw new ValidationException("Invalid workflow type format");

        return sanitizedType;
    }

}