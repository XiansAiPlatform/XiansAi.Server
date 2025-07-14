using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data;

public class Permission : ModelValidatorBase<Permission>
{
    [BsonElement("owner_access")]
    public List<string> OwnerAccess { get; set; } = new();

    [BsonElement("read_access")]
    public List<string> ReadAccess { get; set; } = new();

    [BsonElement("write_access")]
    public List<string> WriteAccess { get; set; } = new();

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

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate that all user IDs in access lists are valid
        var allUserIds = OwnerAccess.Concat(ReadAccess).Concat(WriteAccess).ToList();
        
        foreach (var userId in allUserIds)
        {
            if (!ValidationHelpers.IsValidPattern(userId, ValidationHelpers.Patterns.SafeId))
            {
                throw new ValidationException($"Invalid user ID format: {userId}");
            }
        }

        // Validate that no user appears in multiple access levels
        var ownerUsers = new HashSet<string>(OwnerAccess, StringComparer.OrdinalIgnoreCase);
        var writeUsers = new HashSet<string>(WriteAccess, StringComparer.OrdinalIgnoreCase);
        var readUsers = new HashSet<string>(ReadAccess, StringComparer.OrdinalIgnoreCase);

        var duplicateInWrite = writeUsers.Intersect(ownerUsers).ToList();
        if (duplicateInWrite.Any())
        {
            throw new ValidationException($"Users with owner access should not also have write access: {string.Join(", ", duplicateInWrite)}");
        }

        var duplicateInRead = readUsers.Intersect(ownerUsers.Concat(writeUsers)).ToList();
        if (duplicateInRead.Any())
        {
            throw new ValidationException($"Users with owner/write access should not also have read access: {string.Join(", ", duplicateInRead)}");
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
