using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

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

    /// <summary>
    /// Additional provider identities that resolve to this account, beyond the one in
    /// <see cref="UserId"/>. Exists because <see cref="UserId"/> is the key the rest of the system
    /// stores against — threads, agents, keys, audit trails — so a person who acquires a second
    /// identity (a new provider, or a migration off email-shaped ids) cannot be given a second
    /// record without detaching all of it. Linking maps the new identity onto the existing account
    /// instead.
    ///
    /// Only an administrator may add an entry. A token asserting an unknown subject proves only what
    /// its provider claims; deciding that it belongs to an account already holding access is a
    /// judgement about two identities being the same person, which no token can establish.
    ///
    /// Null rather than empty when there are none, so that records without links stay out of the
    /// unique index on subject and authority.
    /// </summary>
    [BsonElement("linked_identities")]
    [BsonIgnoreIfNull]
    [JsonPropertyName("linkedIdentities")]
    public List<LinkedIdentity>? LinkedIdentities { get; set; }

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

/// <summary>
/// A provider identity that an administrator has attached to an existing account. The pair of
/// subject and authority is what a token presents, and both are needed to match one: a subject is
/// only unique within the issuer that minted it.
/// </summary>
public class LinkedIdentity
{
    /// <summary>The `sub` (or provider-nominated equivalent) claim carried by the token.</summary>
    [BsonElement("subject")]
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Normalized authority that authenticated the subject, stored in the same form as
    /// <see cref="User.ProviderAuthority"/> so the two are comparable.
    /// </summary>
    [BsonElement("authority")]
    [JsonPropertyName("authority")]
    public string Authority { get; set; } = string.Empty;

    [BsonElement("linked_at")]
    [JsonPropertyName("linkedAt")]
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The administrator who made the link, kept so the decision is attributable.</summary>
    [BsonElement("linked_by")]
    [JsonPropertyName("linkedBy")]
    public string LinkedBy { get; set; } = string.Empty;
}

/// <summary>
/// Puts an authority into the one form stored on a <see cref="LinkedIdentity"/>.
///
/// A link is matched by exact equality, both in queries and by the unique index, so the two spellings
/// the rest of the code treats as equal — a trailing slash, and differing case — have to be resolved
/// before storage. Without this, the same provider written two ways would link twice and match
/// neither reliably.
/// </summary>
public static class LinkedIdentityKey
{
    public static string NormalizeAuthority(string? authority) =>
        authority?.Trim().TrimEnd('/').ToLowerInvariant() ?? string.Empty;
}

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
