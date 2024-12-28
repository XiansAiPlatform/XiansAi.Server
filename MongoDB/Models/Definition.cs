using MongoDB.Bson; 
using MongoDB.Bson.Serialization.Attributes;  
public class Definition
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId Id { get; set; }
    
    public string Name { get; set; } = null!;
    
    public string Hash { get; set; } = null!;
    
    public string Source { get; set; } = null!;
    
    public string? Markdown { get; set; }
    
    public List<Activity> Activities { get; set; } = new();
    
    public DateTime CreatedAt { get; set; }
}

public class Activity
{
    public string Name { get; set; } = null!;
    
    public ObjectId AgentId { get; set; }
    
    public List<string> InstructionRefs { get; set; } = new();
}