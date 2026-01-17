using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

public class ParameterDefinition
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("type")]
    public required string Type { get; set; }

    [BsonElement("description")]
    [StringLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    [BsonElement("optional")]
    public bool Optional { get; set; } = false;
}