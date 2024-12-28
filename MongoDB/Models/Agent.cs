using MongoDB.Bson; 
using MongoDB.Bson.Serialization.Attributes;  

public class Agent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId Id { get; set; }
    
    public string Name { get; set; } = null!;
    
    public string Image { get; set; } = null!;
    
    public string? Tag { get; set; }
    
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
}