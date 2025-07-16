using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models;
using Shared.Data.Models.Validation;

namespace Shared.Data;
public class Permission  : ModelValidatorBase<Permission>
{
    [BsonElement("owner_access")]
    public List<string> OwnerAccess { get; set; } = new();

    [BsonElement("read_access")]
    public List<string> ReadAccess { get; set; } = new();

    [BsonElement("write_access")]
    public List<string> WriteAccess { get; set; } = new();

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
    public override Permission SanitizeAndReturn()
    {
        // Create a new permission with sanitized data
        var sanitizedPermission = new Permission
        {
            OwnerAccess = ValidationHelpers.SanitizeStringList(OwnerAccess),
            ReadAccess = ValidationHelpers.SanitizeStringList(ReadAccess),
            WriteAccess = ValidationHelpers.SanitizeStringList(WriteAccess)
        };

        return sanitizedPermission;
    }
}
