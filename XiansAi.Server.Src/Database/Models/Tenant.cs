using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace XiansAi.Server.Database.Models;

public class Tenant
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("domain")]
    public required string Domain { get; set; }

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("logo")]
    public Logo? Logo { get; set; }

    [BsonElement("timezone")]
    public string? Timezone { get; set; }

    [BsonElement("agents")]
    public List<Agent>? Agents { get; set; }

    [BsonElement("permissions")]
    public List<Permission>? Permissions { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public class Logo
{
    [BsonElement("url")]
    public required string Url { get; set; }

    [BsonElement("width")]
    public required int Width { get; set; }

    [BsonElement("height")]
    public required int Height { get; set; }
}

public class Agent
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("is_active")]
    public required bool IsActive { get; set; }

    [BsonElement("flows")]
    public List<Flow>? Flows { get; set; }
}

public class Flow
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("is_active")]
    public required bool IsActive { get; set; }
}

public class Permission
{
    [BsonElement("level")]
    public required string Level { get; set; }

    [BsonElement("owner")]
    public required string Owner { get; set; }
} 