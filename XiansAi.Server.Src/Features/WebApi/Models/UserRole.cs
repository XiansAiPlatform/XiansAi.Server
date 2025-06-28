using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Features.WebApi.Models;

public class UserRole
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("user_id")]
    public required string UserId { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("roles")]
    public List<string> Roles { get; set; } = new();

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    public bool HasRole(string role) => Roles.Contains(role);
    
    public bool IsSysAdmin() => HasRole(SystemRoles.SysAdmin);
    
    public bool IsTenantAdmin() => HasRole(SystemRoles.TenantAdmin);
}