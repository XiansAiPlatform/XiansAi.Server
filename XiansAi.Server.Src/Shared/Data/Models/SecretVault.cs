using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Secret vault document. Key (secret name) is unique in the collection.
/// TenantId/AgentId null = scope across tenants/agents; UserId null = any user can access.
/// ActivationName null = any activation of the agent can access; when set, only that activation can access.
/// </summary>
public class SecretVault
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    /// <summary>Unique secret name/key used to fetch the secret.</summary>
    [BsonElement("key")]
    public required string Key { get; set; }

    /// <summary>
    /// Legacy field. New writes never populate this; the secret value lives in the active
    /// <c>ISecretStoreProvider</c> (e.g. <c>secret_vault_values</c> collection or Azure Key Vault),
    /// keyed by <see cref="Id"/>. Kept nullable so older rows still deserialize and so the
    /// database provider can fall back to reading them during the upgrade window.
    /// </summary>
    [Obsolete("Use ISecretStoreProvider for value storage. Kept only for legacy read fallback.")]
    [BsonElement("encrypted_value")]
    [BsonIgnoreIfNull]
    public string? EncryptedValue { get; set; }

    /// <summary>Null = secret is accessible across all tenants.</summary>
    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }

    /// <summary>Null = secret is accessible across all agents.</summary>
    [BsonElement("agent_id")]
    public string? AgentId { get; set; }

    /// <summary>When set, only this user can access the secret; null = any user.</summary>
    [BsonElement("user_id")]
    public string? UserId { get; set; }

    /// <summary>When set, only this agent activation (by name) can access the secret; null = any activation of the agent can access.</summary>
    [BsonElement("activation_name")]
    public string? ActivationName { get; set; }

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
