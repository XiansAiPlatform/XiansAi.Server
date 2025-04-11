using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Features.WebApi.Models;

public class ParameterDefinition
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("type")]
    public required string Type { get; set; }
}