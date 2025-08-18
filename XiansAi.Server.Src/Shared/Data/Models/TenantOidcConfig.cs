using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public class TenantOidcConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    // Entire OIDC config JSON encrypted as a single string
    [BsonElement("encrypted_payload")]
    public required string EncryptedPayload { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}

