using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

[BsonIgnoreExtraElements]
public class Agent : ModelValidatorBase<Agent>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Agent name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Agent name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("tenant")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tenant must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@|+\-:/\\,#=]+$", ErrorMessage = "Tenant contains invalid characters")]
    [Required(ErrorMessage = "Tenant is required")]
    public required string? Tenant { get; set; }

    [BsonElement("created_by")]  
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Created by contains invalid characters")]
    [Required(ErrorMessage = "Created by is required")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("owner_access")]
    public List<string> OwnerAccess { get; set; } = new();

    [BsonElement("read_access")]
    public List<string> ReadAccess { get; set; } = new();

    [BsonElement("write_access")]
    public List<string> WriteAccess { get; set; } = new();

    [BsonElement("system_scoped")]
    public bool SystemScoped { get; set; } = false;

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
    public override Agent SanitizeAndReturn()
    {
        // Create a new agent with sanitized data
        var sanitizedAgent = new Agent
        {
            Id = this.Id,
            Name = ValidationHelpers.SanitizeString(this.Name),
            Tenant = ValidationHelpers.SanitizeString(this.Tenant),
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy),
            CreatedAt = this.CreatedAt,
            OwnerAccess = ValidationHelpers.SanitizeStringList(this.OwnerAccess),
            ReadAccess = ValidationHelpers.SanitizeStringList(this.ReadAccess),
            WriteAccess = ValidationHelpers.SanitizeStringList(this.WriteAccess)
        };

        return sanitizedAgent;
    }

    public override Agent SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedAgent = this.SanitizeAndReturn();
        sanitizedAgent.Validate();

        return sanitizedAgent;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate agent name format
        // if (!ValidationHelpers.IsValidPattern(Name, va))
        //     throw new ValidationException("Invalid agent name format");

        // Validate dates
        if (!ValidationHelpers.IsValidDate(CreatedAt))
            throw new ValidationException("Agent creation date is invalid");

        if (CreatedAt > DateTime.UtcNow)
            throw new ValidationException("Agent creation date cannot be in the future");

        // Validate access lists
        if (OwnerAccess != null)
        {
            if (!ValidationHelpers.IsValidList(OwnerAccess, item => !string.IsNullOrEmpty(item)))
                throw new ValidationException("Owner access list contains invalid items");
        }

        if (ReadAccess != null)
        {
            if (!ValidationHelpers.IsValidList(ReadAccess, item => !string.IsNullOrEmpty(item)))
                throw new ValidationException("Read access list contains invalid items");
        }

        if (WriteAccess != null)
        {
            if (!ValidationHelpers.IsValidList(WriteAccess, item => !string.IsNullOrEmpty(item)))
                throw new ValidationException("Write access list contains invalid items");
        }
    }



    /// <summary>
    /// Validates and sanitizes an agent name
    /// </summary>
    /// <param name="agentName">The raw agent name to validate and sanitize</param>
    /// <returns>The sanitized agent name</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateName(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
            throw new ValidationException("Agent name is required");

        // Sanitize the agent name
        var sanitizedName = SanitizeName(agentName);

        // Validate the agent name format
        if (!ValidationHelpers.IsValidPattern(sanitizedName, ValidationHelpers.Patterns.AgentNamePattern))
            throw new ValidationException("Invalid agent name format");

        return sanitizedName;
    }
    public static string SanitizeName(string agentName)
    {
        return ValidationHelpers.SanitizeString(agentName);
    }
} 


public enum PermissionLevel
{
    None,
    Read,
    Write,
    Owner
}