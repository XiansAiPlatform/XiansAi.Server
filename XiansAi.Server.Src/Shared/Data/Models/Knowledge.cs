using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

public interface IKnowledge
{
    string? Agent { get; set; }
    string? TenantId { get; set; }
    string Id { get; set; }
    string Name { get; set; }
    string Version { get; set; }
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
} 
public class Knowledge : IKnowledge
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    
    [BsonElement("agent")]
    public string? Agent { get; set; }

    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }
    
    [BsonElement("created_by")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;
    
    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;
    
    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;
    
    [BsonElement("version")]
    public string Version { get; set; } = string.Empty;
    
    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonIgnore]
    public string? PermissionLevel { get; set; }
}