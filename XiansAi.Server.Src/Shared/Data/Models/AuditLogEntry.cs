using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

/// <summary>
/// Records a single user/system action for audit log purposes
/// (who did what, and when).
/// </summary>
[BsonIgnoreExtraElements]
public class AuditLogEntry : ModelValidatorBase<AuditLogEntry>
{
    internal const int MaxDetailEntries = 16;
    internal const int MaxDetailKeyLength = 100;
    internal const int MaxDetailStringLength = 500;
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

    /// <summary>Last time this logical session was seen. First access keeps <see cref="CreatedAt"/>.</summary>
    [BsonElement("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }

    /// <summary>How many times this logical session was recorded, including the first write.</summary>
    [BsonElement("access_count")]
    public int AccessCount { get; set; }

    /// <summary>Hour-bucket key used to collapse concurrent inserts of the same session.</summary>
    [BsonElement("idempotency_key")]
    [JsonIgnore]
    public string? IdempotencyKey { get; set; }

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
            Details = SanitizeDetails(Details),
            CreatedAt = CreatedAt,
            LastSeenAt = LastSeenAt,
            AccessCount = AccessCount,
            IdempotencyKey = IdempotencyKey
        };
    }

    private static Dictionary<string, object?>? SanitizeDetails(Dictionary<string, object?>? details)
    {
        if (details == null)
        {
            return null;
        }

        var sanitized = new Dictionary<string, object?>(Math.Min(details.Count, MaxDetailEntries));
        foreach (var (key, value) in details)
        {
            if (sanitized.Count >= MaxDetailEntries)
            {
                break;
            }

            var sanitizedKey = ValidationHelpers.SanitizeString(key);
            if (string.IsNullOrEmpty(sanitizedKey) || sanitizedKey.Length > MaxDetailKeyLength)
            {
                continue;
            }

            sanitized[sanitizedKey] = SanitizeDetailValue(value);
        }

        return sanitized;
    }

    private static object? SanitizeDetailValue(object? value) =>
        value switch
        {
            null => null,
            string text => SanitizeDetailString(text),
            Dictionary<string, object?> nested => SanitizeDetails(nested),
            _ => value
        };

    private static string SanitizeDetailString(string text)
    {
        var sanitized = ValidationHelpers.SanitizeString(text)
            .Replace("<", string.Empty, StringComparison.Ordinal)
            .Replace(">", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal);

        return sanitized.Length <= MaxDetailStringLength
            ? sanitized
            : sanitized[..MaxDetailStringLength];
    }

    public override AuditLogEntry SanitizeAndValidate()
    {
        var sanitized = SanitizeAndReturn();
        sanitized.Validate();
        return sanitized;
    }
}
