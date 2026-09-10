using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

/// <summary>
/// Records a single user/system action for audit log purposes
/// (who did what, and when).
/// </summary>
[BsonIgnoreExtraElements]
public class AuditLogEntry : ModelValidatorBase<AuditLogEntry>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 100 characters")]
    public required string TenantId { get; set; }

    [BsonElement("participant_id")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Participant ID must be between 1 and 200 characters")]
    public required string ParticipantId { get; set; }

    [BsonElement("logged_in_user")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Logged-in user must be between 1 and 200 characters")]
    public required string LoggedInUser { get; set; }

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

    public override AuditLogEntry SanitizeAndReturn()
    {
        return new AuditLogEntry
        {
            Id = Id,
            TenantId = ValidationHelpers.SanitizeString(TenantId),
            ParticipantId = ValidationHelpers.SanitizeString(ParticipantId),
            LoggedInUser = ValidationHelpers.SanitizeString(LoggedInUser),
            Action = ValidationHelpers.SanitizeString(Action),
            ActivationName = string.IsNullOrEmpty(ActivationName) ? ActivationName : ValidationHelpers.SanitizeString(ActivationName),
            Description = string.IsNullOrEmpty(Description) ? Description : ValidationHelpers.SanitizeString(Description),
            Details = Details,
            CreatedAt = CreatedAt
        };
    }

    public override AuditLogEntry SanitizeAndValidate()
    {
        var sanitized = SanitizeAndReturn();
        sanitized.Validate();
        return sanitized;
    }
}
