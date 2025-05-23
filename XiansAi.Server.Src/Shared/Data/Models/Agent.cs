using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class Agent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("tenant")]
    public required string Tenant { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("permissions")]
    public required Permission Permissions { get; set; }
} 