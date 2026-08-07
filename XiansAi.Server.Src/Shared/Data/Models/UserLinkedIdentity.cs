using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

/// <summary>
/// A provider identity that an administrator, or a trusted-provider sign-in, has attached to an
/// existing account. The pair of subject and authority is what a token presents, and both are needed
/// to match one: a subject is only unique within the issuer that minted it.
///
/// Held in its own collection rather than on the user document so that the unique index over subject
/// and authority covers documents where both fields are always present. Embedded in the user, most
/// records would carry no identities at all, and excluding them would depend on a sparse index —
/// which Azure Cosmos DB does not implement: it treats a missing field as null and counts it toward
/// the constraint, so only one user could ever exist without a link.
/// </summary>
public class UserLinkedIdentity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

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

    /// <summary>The <see cref="User.UserId"/> this identity resolves to.</summary>
    [BsonElement("user_id")]
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("linked_at")]
    [JsonPropertyName("linkedAt")]
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who made the link, kept so the decision is attributable.</summary>
    [BsonElement("linked_by")]
    [JsonPropertyName("linkedBy")]
    public string LinkedBy { get; set; } = string.Empty;
}

/// <summary>
/// Puts an authority into the one form stored on a <see cref="UserLinkedIdentity"/>.
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
