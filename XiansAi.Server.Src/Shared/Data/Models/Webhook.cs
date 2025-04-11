using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Shared.Data.Models
{
    public class Webhook
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public Guid Id { get; set; }
        public required string TenantId { get; set; }
        public required string WorkflowId { get; set; }
        public required string CallbackUrl { get; set; }
        public required string EventType { get; set; }
        public required string Secret { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastTriggeredAt { get; set; }
        public string? CreatedBy { get; set; }
    }
} 