using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Usage;

[BsonIgnoreExtraElements]
public class TokenUsageEvent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [Required]
    public required string TenantId { get; set; }

    [BsonElement("user_id")]
    public string? UserId { get; set; }

    [BsonElement("model")]
    [StringLength(200)]
    public string? Model { get; set; }

    [BsonElement("prompt_tokens")]
    public long PromptTokens { get; set; }

    [BsonElement("completion_tokens")]
    public long CompletionTokens { get; set; }

    [BsonElement("total_tokens")]
    public long TotalTokens { get; set; }

    [BsonElement("message_count")]
    public long MessageCount { get; set; }

    [BsonElement("workflow_id")]
    public string? WorkflowId { get; set; }

    [BsonElement("request_id")]
    public string? RequestId { get; set; }

    [BsonElement("source")]
    public string? Source { get; set; }

    [BsonElement("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

