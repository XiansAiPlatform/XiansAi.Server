using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Models;

/// <summary>
/// Records a single user/system action for audit trail purposes
/// (who did what, and when).
/// </summary>
[BsonIgnoreExtraElements]
public class AuditActivity : ModelValidatorBase<AuditActivity>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 100 characters")]
    public required string TenantId { get; set; }

    [BsonElement("performed_by")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Performed by must be between 1 and 200 characters")]
    public required string PerformedBy { get; set; }

    [BsonElement("action")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Action must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Action contains invalid characters")]
    public required string Action { get; set; }

    [BsonElement("activation_name")]
    [StringLength(100, ErrorMessage = "Activation name must be at most 100 characters")]
    public string? ActivationName { get; set; }

    [BsonElement("description")]
    [StringLength(500, ErrorMessage = "Description must be at most 500 characters")]
    public string? Description { get; set; }

    [BsonElement("details")]
    public Dictionary<string, object?>? Details { get; set; } = [];

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public override AuditActivity SanitizeAndReturn()
    {
        return new AuditActivity
        {
            Id = Id,
            TenantId = ValidationHelpers.SanitizeString(TenantId),
            PerformedBy = ValidationHelpers.SanitizeString(PerformedBy),
            Action = ValidationHelpers.SanitizeString(Action),
            ActivationName = string.IsNullOrEmpty(ActivationName) ? ActivationName : ValidationHelpers.SanitizeString(ActivationName),
            Description = string.IsNullOrEmpty(Description) ? Description : ValidationHelpers.SanitizeString(Description),
            Details = Details,
            CreatedAt = CreatedAt
        };
    }

    public override AuditActivity SanitizeAndValidate()
    {
        var sanitized = SanitizeAndReturn();
        sanitized.Validate();
        return sanitized;
    }
}
