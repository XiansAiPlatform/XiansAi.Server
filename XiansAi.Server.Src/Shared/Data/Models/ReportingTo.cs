using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

/// <summary>
/// Represents a reporting target for an agent.
/// Can be a user, group, or any other entity that receives reports from the agent.
/// </summary>
[BsonIgnoreExtraElements]
public class ReportingTo
{
    /// <summary>
    /// Unique identifier for the reporting target (userId, groupId, email, etc.)
    /// </summary>
    [BsonElement("id")]
    [Required(ErrorMessage = "Reporting target ID is required")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Reporting target ID must be between 1 and 200 characters")]
    public required string Id { get; set; }

    /// <summary>
    /// Simple key-value attributes dictionary for storing additional metadata.
    /// Both keys and values are strings for simplicity.
    /// Examples: { "type": "user", "email": "user@example.com", "name": "John Doe" }
    /// </summary>
    [BsonElement("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    /// <summary>
    /// Timestamp when this reporting target was added.
    /// </summary>
    [BsonElement("added_at")]
    public DateTime? AddedAt { get; set; }

    /// <summary>
    /// User ID who added this reporting target.
    /// </summary>
    [BsonElement("added_by")]
    [StringLength(200)]
    public string? AddedBy { get; set; }
}

