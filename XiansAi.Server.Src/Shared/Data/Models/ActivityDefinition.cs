using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

public class ActivityDefinition : ModelValidatorBase<ActivityDefinition>
{
    [BsonElement("activity_name")]
    [Required(ErrorMessage = "Activity name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Activity name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]+$", ErrorMessage = "Activity name contains invalid characters")]
    public required string ActivityName { get; set; }

    [BsonElement("agent_tool_names")]
    public List<string>? AgentToolNames { get; set; }

    [BsonElement("knowledge_ids")]
    [Required(ErrorMessage = "Knowledge IDs are required")]
    public required List<string> KnowledgeIds { get; set; }

    [BsonElement("parameter_definitions")]
    [Required(ErrorMessage = "Parameter definitions are required")]
    public required List<ParameterDefinition> ParameterDefinitions { get; set; }

    public override ActivityDefinition SanitizeAndReturn()
    {
        // Create a new activity definition with sanitized data
        var sanitizedActivityDefinition = new ActivityDefinition
        {
            ActivityName = ValidationHelpers.SanitizeString(ActivityName),
            AgentToolNames = ValidationHelpers.SanitizeStringList(AgentToolNames),
            KnowledgeIds = ValidationHelpers.SanitizeStringList(KnowledgeIds),
            ParameterDefinitions = ParameterDefinitions?.Select(p => 
            {
                if (p is IModelValidator<ParameterDefinition> validator)
                {
                    return validator.SanitizeAndReturn();
                }
                return p;
            }).ToList() ?? new List<ParameterDefinition>()
        };

        return sanitizedActivityDefinition;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();
        
        // Additional custom validation
        if (!ValidationHelpers.IsValidPattern(ActivityName, ValidationHelpers.Patterns.SafeName))
            throw new ValidationException("Invalid activity name format");
            
        // Validate collections
        if (!ValidationHelpers.IsValidList(KnowledgeIds, id => ValidationHelpers.IsValidPattern(id, ValidationHelpers.Patterns.SafeId)))
            throw new ValidationException("Invalid knowledge ID format in list");
            
        if (!ValidationHelpers.IsValidList(ParameterDefinitions, p => p != null))
            throw new ValidationException("Parameter definitions cannot be null");
    }
}