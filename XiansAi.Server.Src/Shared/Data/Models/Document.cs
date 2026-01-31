using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class Document
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("key")]
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [BsonElement("content")]
    [JsonPropertyName("content")]
    public BsonValue Content { get; set; } = new BsonDocument();

    [BsonElement("metadata")]
    [JsonPropertyName("metadata")]
    public BsonDocument? Metadata { get; set; }

    [BsonElement("agent_id")]
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    [BsonElement("tenant_id")]
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [BsonElement("workflow_id")]
    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; set; }

    [BsonElement("participant_id")]
    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; set; }

    [BsonElement("activation_name")]
    [JsonPropertyName("activationName")]
    public string? ActivationName { get; set; }

    [BsonElement("type")]
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [BsonElement("content_type")]
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [BsonElement("created_at")]
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("expires_at")]
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("created_by")]
    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [BsonElement("updated_by")]
    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }
}
