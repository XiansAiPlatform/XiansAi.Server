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
    
    [BsonElement("leaf_certificate")]
    public required string LeafCertificate { get; set; }
        
    [BsonElement("leaf_certificate_key")]
    public required string LeafCertificateKey { get; set; }

    [BsonElement("root_certificate")]
    public required string RootCertificate { get; set; }

    [BsonElement("root_certificate_key")]
    public required string RootCertificateKey { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}
