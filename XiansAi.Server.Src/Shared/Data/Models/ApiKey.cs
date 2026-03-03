using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Shared.Data.Models
{
    public class ApiKey
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;

        [BsonElement("tenant_id")]
        public required string TenantId { get; set; }

        [BsonElement("name")]
        public required string Name { get; set; } // Label for the key

        [BsonElement("hashed_key")]
        public required string HashedKey { get; set; }

        [BsonElement("created_at")]
        public required DateTime CreatedAt { get; set; }

        [BsonElement("created_by")]
        public required string CreatedBy { get; set; }

        [BsonElement("revoked_at")]
        public DateTime? RevokedAt { get; set; }

        [BsonElement("last_rotated_at")]
        public DateTime? LastRotatedAt { get; set; }

        [BsonElement("agent_name")]
        public string? AgentName { get; set; }

        [BsonElement("activation_name")]
        public string? ActivationName { get; set; }

        [BsonElement("type")]
        public string? Type { get; set; }

        [BsonElement("workflow_name")]
        public string? WorkflowName { get; set; }

        [BsonElement("participant_id")]
        public string? ParticipantId { get; set; }

        [BsonElement("timeout_in_seconds")]
        public int? TimeoutInSeconds { get; set; }

        [BsonElement("webhook_name")]
        public string? WebhookName { get; set; }
    }
}
