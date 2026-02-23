using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Secret vault document. Key (secret name) is unique in the collection.
/// TenantId/AgentId null = scope across tenants/agents; UserId null = any user can access.
/// </summary>
public class SecretVault
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    /// <summary>Unique secret name/key used to fetch the secret.</summary>
    [BsonElement("key")]
    public required string Key { get; set; }

    [BsonElement("encrypted_value")]
    public required string EncryptedValue { get; set; }

    /// <summary>Null = secret is accessible across all tenants.</summary>
    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }

    /// <summary>Null = secret is accessible across all agents.</summary>
    [BsonElement("agent_id")]
    public string? AgentId { get; set; }

    /// <summary>When set, only this user can access the secret; null = any user.</summary>
    [BsonElement("user_id")]
    public string? UserId { get; set; }

    /// <summary>Optional JSON for additional metadata.</summary>
    [BsonElement("additional_data")]
    public string? AdditionalData { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}
