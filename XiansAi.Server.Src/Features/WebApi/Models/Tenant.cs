using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;
using Shared.Data.Models;

namespace Features.WebApi.Models;

public class Tenant
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("domain")]
    public required string Domain { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("logo")]
    public Logo? Logo { get; set; }

    [BsonElement("theme")]
    public string? Theme { get; set; }

    [BsonElement("timezone")]
    public string? Timezone { get; set; }

    [BsonElement("agents")]
    public List<Agent>? Agents { get; set; }

    [BsonElement("permissions")]
    public List<Permission>? Permissions { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("enabled")]
    public bool Enabled { get; set; } = true; // Default to enabled
}

public class Logo
{
    [BsonElement("url")]
    public string? Url { get; set; }

    [BsonElement("img_base64")]
    public string? ImgBase64 { get; set; }

    [BsonElement("width")]
    public required int Width { get; set; }

    [BsonElement("height")]
    public required int Height { get; set; }
}

public class Flow
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("is_active")]
    public required bool IsActive { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }
}
