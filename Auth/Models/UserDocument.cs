using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace XiansAi.Server.Auth.Models
{
    public class TenantMembership
    {
        public string TenantId { get; set; } = string.Empty;
        public string Role { get; set; } = "TenantUser"; // TenantAdmin or TenantUser
    }

    public class UserDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        public string Email { get; set; } = string.Empty;
        
        public string Auth0Id { get; set; } = string.Empty;
        
        public bool IsSystemAdmin { get; set; } = false;
        
        public List<TenantMembership> TenantMemberships { get; set; } = new List<TenantMembership>();
    }
} 