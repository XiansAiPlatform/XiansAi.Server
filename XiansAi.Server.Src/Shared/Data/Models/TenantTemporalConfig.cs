using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Stores the dedicated Temporal connection details a tenant admin supplied when
/// enabling "Use a specific Temporal namespace for this tenant". Certificate and
/// key are optional but must be provided together (mTLS); omit both to connect
/// without TLS client auth.
/// </summary>
public class TenantTemporalConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("host")]
    public required string Host { get; set; }

    [BsonElement("namespace")]
    public required string Namespace { get; set; }

    [BsonElement("certificate")]
    public string? Certificate { get; set; }

    [BsonElement("certificate_key")]
    public string? CertificateKey { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}
