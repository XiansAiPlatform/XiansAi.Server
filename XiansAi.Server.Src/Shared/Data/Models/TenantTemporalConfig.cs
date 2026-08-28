using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public class TenantTemporalConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("server_url")]
    public required string ServerUrl { get; set; }

    [BsonElement("namespace")]
    public required string Namespace { get; set; }
    
    [BsonElement("certificate")]
    public string? Certificate { get; set; }

    [BsonElement("private_key")]
    public string? PrivateKey { get; set; }

    [BsonElement("is_reverted")]
    public bool IsReverted { get; set; } = false;

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public required DateTime UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public required string UpdatedBy { get; set; }

    [BsonElement("reverted_at")]
    public DateTime? RevertedAt { get; set; } = null;

    [BsonElement("reverted_by")]
    public string? RevertedBy { get; set; } = null;
}
