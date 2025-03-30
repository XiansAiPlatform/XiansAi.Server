using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Generic;

namespace XiansAi.Server.Auth.Models
{
    public class UserPermission
    {
        public string UserId { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty; // Owner, Editor, Reader
    }

    public class PermissionDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;
        
        public string EntityId { get; set; } = string.Empty;
        
        public string EntityType { get; set; } = string.Empty; // Flow, Activity, etc.
        
        public string TenantId { get; set; } = string.Empty;
        
        public List<UserPermission> Permissions { get; set; } = new List<UserPermission>();
    }
} 