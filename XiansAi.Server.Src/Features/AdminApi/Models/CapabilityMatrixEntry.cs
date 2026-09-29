using Features.AdminApi.Auth;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Features.AdminApi.Models;

/// <summary>
/// One row of the AdminApi capability matrix: the roles allowed to perform a single named action
/// (see <see cref="CapabilityActions"/>).
/// </summary>
public class CapabilityMatrixEntry
{
    /// <summary>Populated on read only. Writes go through field-wise updates that never send it.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    /// <summary>One of the <see cref="CapabilityActions"/> constants, e.g. "tenant.users.delete".</summary>
    [BsonElement("action")]
    public required string Action { get; set; }

    [BsonElement("allowed_roles")]
    public required List<string> AllowedRoles { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}
