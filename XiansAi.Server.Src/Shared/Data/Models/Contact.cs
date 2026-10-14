using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// A tenant-scoped notification contact (name, email, phone).
/// </summary>
public class Contact
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("email")]
    public required string Email { get; set; }

    [BsonElement("phone")]
    public string? Phone { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}
