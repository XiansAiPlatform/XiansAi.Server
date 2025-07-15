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

    [BsonElement("email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("tenant_roles")]
    [JsonPropertyName("tenantRoles")]
    public List<TenantRole> TenantRoles { get; set; } = new();

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
