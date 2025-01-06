using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

public class ActivityDefinition
{
    [BsonElement("activity_name")]
    public required string ActivityName { get; set; }

    [BsonElement("docker_image")]
    public string? DockerImage { get; set; }

    [BsonElement("instructions")]
    public required List<string> Instructions { get; set; }

    [BsonElement("parameters")]
    public required List<ParameterDefinition> Parameters { get; set; }
}