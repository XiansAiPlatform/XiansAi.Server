using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// MongoDB document model for secrets stored in the database provider.
/// For Key Vault providers, this model is not used - secrets are stored as JSON in the vault.
/// </summary>
public class Secret
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("secret_id")]
    public required string SecretId { get; set; }

    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }

    [BsonElement("agent_id")]
    public string? AgentId { get; set; }

    [BsonElement("user_id")]
    public string? UserId { get; set; }

    // Encrypted fields
    [BsonElement("encrypted_secret_value")]
    public required string EncryptedSecretValue { get; set; }

    [BsonElement("encrypted_metadata")]
    public string? EncryptedMetadata { get; set; }

    [BsonElement("encrypted_expire_at")]
    public string? EncryptedExpireAt { get; set; }

    // Non-sensitive fields
    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Canonical secret object used across all providers.
/// For Key Vault providers, this entire object is serialized to JSON and stored as the vault secret value.
/// </summary>
public class SecretData
{
    public required string SecretId { get; set; }
    public string? TenantId { get; set; }
    public string? AgentId { get; set; }
    public string? UserId { get; set; }
    public required string SecretValue { get; set; }
    public string? Metadata { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpireAt { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

