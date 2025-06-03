using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class Agent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("tenant")]
    public required string Tenant { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("owner_access")]
    public List<string> OwnerAccess { get; set; } = new();

    [BsonElement("read_access")]
    public List<string> ReadAccess { get; set; } = new();

    [BsonElement("write_access")]
    public List<string> WriteAccess { get; set; } = new();

    public Permission? Permissions {
        get {
            return new Permission() {
                OwnerAccess = OwnerAccess,
                ReadAccess = ReadAccess,
                WriteAccess = WriteAccess
            };
        }
    }

    public bool HasPermission(string userId, string[] userRoles, PermissionLevel requiredLevel)
    {
        if (requiredLevel == PermissionLevel.None)
            return true;

        if (requiredLevel == PermissionLevel.Owner)
            return OwnerAccess.Contains(userId);

        if (requiredLevel == PermissionLevel.Write)
            return WriteAccess.Contains(userId) || OwnerAccess.Contains(userId);

        if (requiredLevel == PermissionLevel.Read)
            return ReadAccess.Contains(userId) || WriteAccess.Contains(userId) || OwnerAccess.Contains(userId);

        return false;
    }

    public void GrantReadAccess(string userId)
    {
        if (!ReadAccess.Contains(userId))
            ReadAccess.Add(userId);
    }

    public void RevokeReadAccess(string userId)
    {
        ReadAccess.Remove(userId);
    }

    public void GrantWriteAccess(string userId)
    {
        if (!WriteAccess.Contains(userId))
            WriteAccess.Add(userId);
    }

    public void RevokeWriteAccess(string userId)
    {
        WriteAccess.Remove(userId);
    }

    public void GrantOwnerAccess(string userId)
    {
        if (!OwnerAccess.Contains(userId))
            OwnerAccess.Add(userId);
    }

    public void RevokeOwnerAccess(string userId)
    {
        OwnerAccess.Remove(userId);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
} 


public enum PermissionLevel
{
    None,
    Read,
    Write,
    Owner
}