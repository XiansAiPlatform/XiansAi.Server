using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models.Usage;

[BsonIgnoreExtraElements]
public class TokenUsageLimit
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [Required]
    [StringLength(100)]
    public required string TenantId { get; set; }

    [BsonElement("user_id")]
    [StringLength(100)]
    public string? UserId { get; set; }

    [BsonElement("max_tokens")]
    [Range(1, long.MaxValue)]
    public long MaxTokens { get; set; }

    [BsonElement("window_seconds")]
    [Range(60, 60 * 60 * 24 * 30)] // up to 30 days
    public int WindowSeconds { get; set; }

    [BsonElement("effective_from")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    [BsonElement("enabled")]
    public bool Enabled { get; set; } = true;

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_by")]
    [StringLength(100)]
    public string? UpdatedBy { get; set; }
}

[BsonIgnoreExtraElements]
public class TokenUsageWindow
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("tenant_id")]
    [Required]
    public required string TenantId { get; set; }

    [BsonElement("user_id")]
    [Required]
    public required string UserId { get; set; }

    [BsonElement("window_start")]
    public DateTime WindowStart { get; set; }

    [BsonElement("window_seconds")]
    public int WindowSeconds { get; set; }

    [BsonElement("tokens_used")]
    public long TokensUsed { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

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

