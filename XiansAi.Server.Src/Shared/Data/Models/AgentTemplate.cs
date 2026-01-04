using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization;

namespace Shared.Data.Models;

/// <summary>
/// Represents a system-scoped agent template (reusable agent definition).
/// Agent templates are stored in the agent_templates collection and can be deployed to tenants as Agent instances.
/// </summary>
[BsonIgnoreExtraElements]
public class AgentTemplate : ModelValidatorBase<AgentTemplate>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Template name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Template name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("created_by")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Created by contains invalid characters")]
    [Required(ErrorMessage = "Created by is required")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Onboarding JSON for the template.
    /// </summary>
    [BsonElement("onboarding_json")]
    public string? OnboardingJson { get; set; }

    /// <summary>
    /// Metadata dictionary for flexible storage of template-specific data.
    /// Can contain: Category, SamplePrompts, Description, Icon, etc.
    /// </summary>
    [BsonElement("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Owner access list (who can manage the template).
    /// </summary>
    [BsonElement("owner_access")]
    public List<string> OwnerAccess { get; set; } = new();

    /// <summary>
    /// Read access list.
    /// </summary>
    [BsonElement("read_access")]
    public List<string> ReadAccess { get; set; } = new();

    /// <summary>
    /// Write access list.
    /// </summary>
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

    /// <summary>
    /// Gets the category from metadata.
    /// Returns a single category string (may contain comma-separated values like "customer-support,beta").
    /// </summary>
    public string? GetCategory()
    {
        return Metadata?.TryGetValue("category", out var value) == true ? value?.ToString() : null;
    }

    /// <summary>
    /// Gets categories as a list (splits comma-separated category string).
    /// Example: "customer-support,beta" returns ["customer-support", "beta"]
    /// </summary>
    public List<string> GetCategories()
    {
        var category = GetCategory();
        if (string.IsNullOrWhiteSpace(category))
            return new List<string>();
        
        return category.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
    }

    /// <summary>
    /// Sets the category in metadata.
    /// Supports single category or comma-separated multiple categories (e.g., "customer-support,beta").
    /// </summary>
    public void SetCategory(string? category)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(category))
            Metadata.Remove("category");
        else
            Metadata["category"] = category.Trim();
    }

    /// <summary>
    /// Sets multiple categories in metadata (stored as comma-separated string).
    /// </summary>
    public void SetCategories(List<string>? categories)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (categories == null || categories.Count == 0)
        {
            Metadata.Remove("category");
            return;
        }
        
        var categoryString = string.Join(",", categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim()));
        
        if (string.IsNullOrWhiteSpace(categoryString))
            Metadata.Remove("category");
        else
            Metadata["category"] = categoryString;
    }

    /// <summary>
    /// Gets sample prompts from metadata.
    /// </summary>
    public List<SamplePrompt> GetSamplePrompts()
    {
        if (Metadata?.TryGetValue("samplePrompts", out var value) == true)
        {
            // Try to deserialize from BSON array or JSON
            if (value is List<object> list)
            {
                var prompts = new List<SamplePrompt>();
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> dict)
                    {
                        prompts.Add(new SamplePrompt
                        {
                            Id = dict.TryGetValue("id", out var id) ? id?.ToString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                            Title = dict.TryGetValue("title", out var title) ? title?.ToString() ?? string.Empty : string.Empty,
                            Prompt = dict.TryGetValue("prompt", out var prompt) ? prompt?.ToString() ?? string.Empty : string.Empty,
                            Description = dict.TryGetValue("description", out var desc) ? desc?.ToString() : null,
                            Icon = dict.TryGetValue("icon", out var icon) ? icon?.ToString() : null,
                            Category = dict.TryGetValue("category", out var cat) ? cat?.ToString() : null
                        });
                    }
                }
                return prompts;
            }
        }
        return new List<SamplePrompt>();
    }

    /// <summary>
    /// Sets sample prompts in metadata.
    /// Converts SamplePrompt objects to BSON-serializable format (list of dictionaries).
    /// </summary>
    public void SetSamplePrompts(List<SamplePrompt> samplePrompts)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        
        if (samplePrompts == null || samplePrompts.Count == 0)
        {
            Metadata["samplePrompts"] = new List<Dictionary<string, object>>();
            return;
        }

        // Convert List<SamplePrompt> to List<Dictionary<string, object>> for MongoDB serialization
        var serializableList = samplePrompts.Select(prompt =>
        {
            var dict = new Dictionary<string, object>
            {
                { "id", prompt.Id },
                { "title", prompt.Title },
                { "prompt", prompt.Prompt }
            };
            
            // Only add optional fields if they have values (MongoDB handles null automatically)
            if (!string.IsNullOrEmpty(prompt.Description))
                dict["description"] = prompt.Description;
            if (!string.IsNullOrEmpty(prompt.Icon))
                dict["icon"] = prompt.Icon;
            if (!string.IsNullOrEmpty(prompt.Category))
                dict["category"] = prompt.Category;
                
            return dict;
        }).ToList();

        Metadata["samplePrompts"] = serializableList;
    }

    /// <summary>
    /// Gets the description from metadata.
    /// </summary>
    public string? GetDescription()
    {
        return Metadata?.TryGetValue("description", out var value) == true ? value?.ToString() : null;
    }

    /// <summary>
    /// Sets the description in metadata.
    /// </summary>
    public void SetDescription(string? description)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(description))
            Metadata.Remove("description");
        else
            Metadata["description"] = description;
    }

    /// <summary>
    /// Gets the icon from metadata.
    /// </summary>
    public string? GetIcon()
    {
        return Metadata?.TryGetValue("icon", out var value) == true ? value?.ToString() : null;
    }

    /// <summary>
    /// Sets the icon in metadata.
    /// </summary>
    public void SetIcon(string? icon)
    {
        if (Metadata == null) Metadata = new Dictionary<string, object>();
        if (string.IsNullOrWhiteSpace(icon))
            Metadata.Remove("icon");
        else
            Metadata["icon"] = icon;
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }

    public override AgentTemplate SanitizeAndReturn()
    {
        var sanitizedTemplate = new AgentTemplate
        {
            Id = this.Id,
            Name = ValidationHelpers.SanitizeString(this.Name),
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy),
            CreatedAt = this.CreatedAt,
            OnboardingJson = ValidationHelpers.SanitizeString(this.OnboardingJson),
            OwnerAccess = ValidationHelpers.SanitizeStringList(this.OwnerAccess),
            ReadAccess = ValidationHelpers.SanitizeStringList(this.ReadAccess),
            WriteAccess = ValidationHelpers.SanitizeStringList(this.WriteAccess),
            Metadata = this.Metadata  // Note: Dictionary sanitization may need custom logic
        };

        return sanitizedTemplate;
    }

    public override AgentTemplate SanitizeAndValidate()
    {
        var sanitizedTemplate = this.SanitizeAndReturn();
        sanitizedTemplate.Validate();
        return sanitizedTemplate;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate dates
        if (!ValidationHelpers.IsValidDate(CreatedAt))
            throw new ValidationException("Template creation date is invalid");

        if (CreatedAt > DateTime.UtcNow)
            throw new ValidationException("Template creation date cannot be in the future");

        // Validate category in metadata if provided
        if (Metadata?.TryGetValue("category", out var categoryValue) == true && categoryValue != null)
        {
            var category = categoryValue.ToString();
            if (!string.IsNullOrWhiteSpace(category))
            {
                var validCategories = new[] { "marketing", "sales", "service" };
                if (!validCategories.Contains(category.ToLowerInvariant()))
                {
                    throw new ValidationException($"Invalid category '{category}'. Valid categories are: {string.Join(", ", validCategories)}");
                }
            }
        }

        // Validate sample prompts in metadata
        if (Metadata?.TryGetValue("samplePrompts", out var promptsValue) == true && promptsValue is List<object> promptsList)
        {
            foreach (var promptObj in promptsList)
            {
                // Note: SamplePrompt validation would need to be done when deserializing from metadata
                // For now, we just check that it's not null
                if (promptObj == null)
                    throw new ValidationException("Sample prompt cannot be null");
            }
        }

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
    }

    /// <summary>
    /// Validates and sanitizes a template name
    /// </summary>
    public static string SanitizeAndValidateName(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new ValidationException("Template name is required");

        var sanitizedName = ValidationHelpers.SanitizeString(templateName);

        if (!ValidationHelpers.IsValidPattern(sanitizedName, ValidationHelpers.Patterns.AgentNamePattern))
            throw new ValidationException("Invalid template name format");

        return sanitizedName;
    }

    public static string SanitizeName(string templateName)
    {
        return ValidationHelpers.SanitizeString(templateName);
    }
}

