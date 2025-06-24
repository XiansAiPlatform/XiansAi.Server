using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Features.WebApi.Models
{
    public class UserTenant
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;

        [BsonElement("tenant_id")]
        public required string TenantId { get; set; }

        [BsonElement("user_id")]
        public required string UserId { get; set; }
    }
}
