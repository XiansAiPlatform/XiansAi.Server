using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

// Documents may carry fields written by newer server versions; ignoring them keeps reads working.
[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("user_id")]
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    private string _email = string.Empty;
    
    [BsonElement("email")]
    [JsonPropertyName("email")]
    public string Email 
    { 
        get => _email;
        set => _email = value.ToLowerInvariant();
    }

    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("tenant_roles")]
    [JsonPropertyName("tenantRoles")]
    public List<TenantRole> TenantRoles { get; set; } = new();

    /// <summary>
    /// Normalized OIDC authority whose signing keys first authenticated this subject. Pinned
    /// because <see cref="UserId"/> holds a provider subject, which OIDC only guarantees to be
    /// unique within one issuer — without the pin, a second provider asserting the same subject
    /// resolves to this same record.
    ///
    /// This is deliberately the authority (where the signing keys are fetched from) rather than
    /// the token's `iss` claim: the expected issuer comes from tenant-supplied configuration and
    /// can name any string, whereas the authority must actually serve the discovery document, so
    /// it cannot be pointed at a provider the configurer does not control.
    ///
    /// Null on records created before pinning existed and on paths that do not set it; those are
    /// pinned on first use.
    /// </summary>
    [BsonElement("provider_authority")]
    [JsonPropertyName("providerAuthority")]
    public string? ProviderAuthority { get; set; }

    [BsonElement("is_sys_admin")]
    [JsonPropertyName("isSysAdmin")]
    public bool IsSysAdmin { get; set; }

    [BsonElement("is_locked_out")]
    [JsonPropertyName("isLockedOut")]
    public bool IsLockedOut { get; set; }

    [BsonElement("locked_out_reason")]
    [JsonPropertyName("lockedOutReason")]
    public string? LockedOutReason { get; set; }

    [BsonElement("locked_out_at")]
    [JsonPropertyName("lockedOutAt")]
    public DateTime? LockedOutAt { get; set; }

    [BsonElement("locked_out_by")]
    [JsonPropertyName("lockedOutBy")]
    public string? LockedOutBy { get; set; }

    [BsonElement("created_at")]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class TenantRole
{
    [BsonElement("tenant")]
    [JsonPropertyName("tenant")]
    public string Tenant { get; set; } = string.Empty;

    [BsonElement("roles")]
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [BsonElement("is_approved")]
    public required bool IsApproved { get; set; }
}
