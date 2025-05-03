using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Shared.Data.Models;

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
}

public enum InstructionType
{
    json,
    text,
    markdown
} 