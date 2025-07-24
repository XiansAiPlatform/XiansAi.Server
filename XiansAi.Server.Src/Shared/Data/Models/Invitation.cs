using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

public class Invitation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("email")]
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("roles")]
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    [BsonElement("token")]
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [BsonElement("status")]
    [JsonPropertyName("status")]
    public Status Status { get; set; } = Status.Pending;

    [BsonElement("expires_at")]
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("created_at")]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum Status
{
    Pending,
    Accepted,
    Expired
}