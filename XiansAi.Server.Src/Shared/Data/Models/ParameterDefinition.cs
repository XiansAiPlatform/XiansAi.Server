using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

public class ParameterDefinition : ModelValidatorBase<ParameterDefinition>
{
    [BsonElement("name")]
    [Required(ErrorMessage = "Parameter name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Parameter name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]+$", ErrorMessage = "Parameter name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("type")]
    [Required(ErrorMessage = "Parameter type is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Parameter type must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Parameter type contains invalid characters")]
    public required string Type { get; set; }

    public override ParameterDefinition SanitizeAndReturn()
    {
        // Create a new parameter definition with sanitized data
        var sanitizedParameterDefinition = new ParameterDefinition
        {
            Name = ValidationHelpers.SanitizeString(Name),
            Type = ValidationHelpers.SanitizeString(Type)
        };

        return sanitizedParameterDefinition;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();
               
    }
}