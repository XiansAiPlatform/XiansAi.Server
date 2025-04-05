using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Features.AgentApi.Data.Models;

public class Instruction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("version")]
    public required string Version { get; set; }

    [BsonElement("content")]
    public required string Content { get; set; }

    [BsonElement("type")]
    public required string Type { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }
}

public enum InstructionType
{
    json,
    text,
    markdown
} 