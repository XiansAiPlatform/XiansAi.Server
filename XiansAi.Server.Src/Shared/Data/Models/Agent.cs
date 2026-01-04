using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;
using Shared.Data.Models;
using Shared.Utils.Serialization;

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

    [BsonElement("onboarding_json")]
    public string? OnboardingJson { get; set; }

    /// <summary>
    /// Reference to the agent template ID (if deployed from template).
    /// Optional field for backward compatibility.
    /// </summary>
    [BsonElement("agent_template_id")]
    [StringLength(100, ErrorMessage = "Agent template ID must be less than 100 characters")]
    public string? AgentTemplateId { get; set; }

    /// <summary>
    /// Metadata dictionary for flexible storage of instance-specific data.
    /// Can contain: adminId, reportingUsers, deployedAt, deployedById, configuration, etc.
    /// Optional field for backward compatibility.
    /// </summary>
    [BsonElement("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

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

    /// <summary>
    /// Gets the admin ID from metadata (for instances).
    /// </summary>
    public string? GetAdminId()
    {
        return Metadata?.TryGetValue("adminId", out var value) == true ? value?.ToString() : null;
    }

    /// <summary>
    /// Sets the admin ID in metadata (for instances).
    /// </summary>
    public void SetAdminId(string adminId)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        Metadata["adminId"] = adminId;
    }

    /// <summary>
    /// Gets reporting targets from metadata (for instances).
    /// Returns list of ReportingTo objects (can be users, groups, etc.)
    /// </summary>
    public List<ReportingTo> GetReportingTargets()
    {
        if (Metadata?.TryGetValue("reportingTargets", out var value) == true)
        {
            // Handle new structure: List<ReportingTo>
            if (value is List<object> list)
            {
                var result = new List<ReportingTo>();
                foreach (var item in list)
                {
                    if (item is ReportingTo reportingTo)
                    {
                        result.Add(reportingTo);
                    }
                    else if (item is Dictionary<string, object> dict)
                    {
                        // Deserialize from dictionary
                        var targetId = dict.TryGetValue("id", out var id) ? id?.ToString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrEmpty(targetId))
                        {
                            // Convert attributes from Dictionary<string, object> to Dictionary<string, string>
                            Dictionary<string, string>? attributes = null;
                            if (dict.TryGetValue("attributes", out var attrs))
                            {
                                if (attrs is Dictionary<string, string> stringDict)
                                {
                                    attributes = stringDict;
                                }
                                else if (attrs is Dictionary<string, object> objectDict)
                                {
                                    // Convert Dictionary<string, object> to Dictionary<string, string>
                                    attributes = objectDict.ToDictionary(
                                        kvp => kvp.Key,
                                        kvp => kvp.Value?.ToString() ?? string.Empty);
                                }
                            }

                            var reportingTarget = new ReportingTo
                            {
                                Id = targetId,
                                Attributes = attributes,
                                AddedAt = dict.TryGetValue("added_at", out var addedAt) && addedAt is DateTime dt 
                                    ? dt 
                                    : (dict.TryGetValue("added_at", out var addedAtStr) && DateTime.TryParse(addedAtStr?.ToString(), out var parsed) 
                                        ? parsed 
                                        : null),
                                AddedBy = dict.TryGetValue("added_by", out var addedBy) ? addedBy?.ToString() : null
                            };
                            result.Add(reportingTarget);
                        }
                    }
                }
                return result;
            }
        }
        
        // Backward compatibility: Check for old "reportingUsers" field (List<string>)
        if (Metadata?.TryGetValue("reportingUsers", out var oldValue) == true && oldValue is List<object> oldList)
        {
            return oldList
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(userId => new ReportingTo 
                { 
                    Id = userId!, 
                    Attributes = new Dictionary<string, string> { { "type", "user" } }
                })
                .ToList();
        }
        
        return new List<ReportingTo>();
    }

    /// <summary>
    /// Sets reporting targets in metadata (for instances).
    /// Converts ReportingTo objects to BSON-serializable format (list of dictionaries).
    /// </summary>
    public void SetReportingTargets(List<ReportingTo> reportingTargets)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        
        if (reportingTargets == null || reportingTargets.Count == 0)
        {
            Metadata["reportingTargets"] = new List<Dictionary<string, object>>();
            // Remove old "reportingUsers" field for migration
            Metadata.Remove("reportingUsers");
            return;
        }

        // Convert List<ReportingTo> to List<Dictionary<string, object>> for MongoDB serialization
        var serializableList = reportingTargets.Select(target =>
        {
            var dict = new Dictionary<string, object>
            {
                { "id", target.Id }
            };
            
            // Add optional fields if they have values
            // Attributes is now Dictionary<string, string> - MongoDB can serialize this directly
            if (target.Attributes != null && target.Attributes.Count > 0)
            {
                dict["attributes"] = target.Attributes;
            }
            if (target.AddedAt.HasValue)
                dict["added_at"] = target.AddedAt.Value;
            if (!string.IsNullOrEmpty(target.AddedBy))
                dict["added_by"] = target.AddedBy;
                
            return dict;
        }).ToList();

        Metadata["reportingTargets"] = serializableList;
        
        // Remove old "reportingUsers" field for migration
        Metadata.Remove("reportingUsers");
    }

    /// <summary>
    /// Adds a reporting target to metadata (for instances).
    /// </summary>
    public void AddReportingTarget(ReportingTo reportingTarget)
    {
        if (reportingTarget == null || string.IsNullOrWhiteSpace(reportingTarget.Id))
            return;
            
        var reportingTargets = GetReportingTargets();
        if (!reportingTargets.Any(rt => rt.Id.Equals(reportingTarget.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (reportingTarget.AddedAt == null)
                reportingTarget.AddedAt = DateTime.UtcNow;
            reportingTargets.Add(reportingTarget);
            SetReportingTargets(reportingTargets);
        }
    }

    /// <summary>
    /// Removes a reporting target from metadata (for instances).
    /// </summary>
    public void RemoveReportingTarget(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return;
            
        var reportingTargets = GetReportingTargets();
        reportingTargets.RemoveAll(rt => rt.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
        SetReportingTargets(reportingTargets);
    }

    /// <summary>
    /// Legacy method: Gets reporting users as list of IDs (for backward compatibility).
    /// </summary>
    [Obsolete("Use GetReportingTargets() instead")]
    public List<string> GetReportingUsers()
    {
        return GetReportingTargets().Select(rt => rt.Id).ToList();
    }

    /// <summary>
    /// Legacy method: Sets reporting users from list of IDs (for backward compatibility).
    /// </summary>
    [Obsolete("Use SetReportingTargets() instead")]
    public void SetReportingUsers(List<string> reportingUsers)
    {
        var reportingTargets = (reportingUsers ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(userId => new ReportingTo 
            { 
                Id = userId, 
                Attributes = new Dictionary<string, string> { { "type", "user" } },
                AddedAt = DateTime.UtcNow
            })
            .ToList();
        SetReportingTargets(reportingTargets);
    }

    /// <summary>
    /// Legacy method: Adds a reporting user by ID (for backward compatibility).
    /// </summary>
    [Obsolete("Use AddReportingTarget() instead")]
    public void AddReportingUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;
            
        AddReportingTarget(new ReportingTo 
        { 
            Id = userId, 
            Attributes = new Dictionary<string, string> { { "type", "user" } },
            AddedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Legacy method: Removes a reporting user by ID (for backward compatibility).
    /// </summary>
    [Obsolete("Use RemoveReportingTarget() instead")]
    public void RemoveReportingUser(string userId)
    {
        RemoveReportingTarget(userId);
    }


    /// <summary>
    /// Gets the deployed at timestamp from metadata (for instances).
    /// </summary>
    public DateTime? GetDeployedAt()
    {
        if (Metadata?.TryGetValue("deployedAt", out var value) == true)
        {
            if (value is DateTime dt) return dt;
            if (value is string str && DateTime.TryParse(str, out var parsed)) return parsed;
        }
        return null;
    }

    /// <summary>
    /// Sets the deployed at timestamp in metadata (for instances).
    /// </summary>
    public void SetDeployedAt(DateTime? deployedAt)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (deployedAt == null)
            Metadata.Remove("deployedAt");
        else
            Metadata["deployedAt"] = deployedAt.Value;
    }

    /// <summary>
    /// Gets the deployed by ID from metadata (for instances).
    /// </summary>
    public string? GetDeployedById()
    {
        return Metadata?.TryGetValue("deployedById", out var value) == true ? value?.ToString() : null;
    }

    /// <summary>
    /// Sets the deployed by ID in metadata (for instances).
    /// </summary>
    public void SetDeployedById(string? deployedById)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(deployedById))
            Metadata.Remove("deployedById");
        else
            Metadata["deployedById"] = deployedById;
    }

    /// <summary>
    /// Gets the configuration dictionary from metadata (for instances).
    /// </summary>
    public Dictionary<string, object>? GetConfiguration()
    {
        if (Metadata?.TryGetValue("configuration", out var value) == true && value is Dictionary<string, object> config)
        {
            return config;
        }
        return null;
    }

    /// <summary>
    /// Sets the configuration dictionary in metadata (for instances).
    /// </summary>
    public void SetConfiguration(Dictionary<string, object>? configuration)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (configuration == null)
            Metadata.Remove("configuration");
        else
            Metadata["configuration"] = configuration;
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
            WriteAccess = ValidationHelpers.SanitizeStringList(this.WriteAccess),
            SystemScoped = this.SystemScoped,
            OnboardingJson = ValidationHelpers.SanitizeString(this.OnboardingJson),
            AgentTemplateId = ValidationHelpers.SanitizeString(this.AgentTemplateId),
            Metadata = this.Metadata  // Note: Dictionary sanitization may need custom logic
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

        // Validate onboarding JSON if provided
        if (!string.IsNullOrEmpty(OnboardingJson))
        {
            try
            {
                JsonSerializer.Deserialize<JsonElement>(OnboardingJson);
            }
            catch (JsonException)
            {
                throw new ValidationException("OnboardingJson contains invalid JSON format");
            }
        }

        // Validate reporting targets in metadata (for instances)
        var reportingTargets = GetReportingTargets();
        foreach (var target in reportingTargets)
        {
            if (string.IsNullOrWhiteSpace(target.Id))
                throw new ValidationException("Reporting targets list contains empty identifier");
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