using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Models;

[BsonIgnoreExtraElements]
public class Tenant : ModelValidatorBase
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "Tenant ID is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Tenant ID contains invalid characters")]
    public required string Id { get; set; }

    [BsonElement("tenant_id")]
    [Required(ErrorMessage = "Tenant ID is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Tenant ID contains invalid characters")]
    public required string TenantId { get; set; }

    [BsonElement("name")]
    [Required(ErrorMessage = "Tenant name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tenant name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]+$", ErrorMessage = "Tenant name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("domain")]
    [Required(ErrorMessage = "Domain is required")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "Domain must be between 1 and 255 characters")]
    [RegularExpression(@"^[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", ErrorMessage = "Domain format is invalid")]
    public required string Domain { get; set; }

    [BsonElement("description")]
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]*$", ErrorMessage = "Description contains invalid characters")]
    public string? Description { get; set; }

    [BsonElement("logo")]
    public Logo? Logo { get; set; }

    [BsonElement("theme")]
    [StringLength(50, ErrorMessage = "Theme cannot exceed 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._-]*$", ErrorMessage = "Theme contains invalid characters")]
    public string? Theme { get; set; }

    [BsonElement("timezone")]
    [StringLength(50, ErrorMessage = "Timezone cannot exceed 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9/._-]*$", ErrorMessage = "Timezone contains invalid characters")]
    public string? Timezone { get; set; }

    [BsonElement("agents")]
    public List<Agent>? Agents { get; set; }

    [BsonElement("permissions")]
    public List<Permission>? Permissions { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    [Required(ErrorMessage = "Created by is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Created by contains invalid characters")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("enabled")]
    public bool Enabled { get; set; } = true;

    public override void Sanitize()
    {
        // Sanitize string properties
        Id = ValidationHelpers.SanitizeString(Id);
        TenantId = ValidationHelpers.SanitizeString(TenantId);
        Name = ValidationHelpers.SanitizeString(Name);
        Domain = ValidationHelpers.SanitizeString(Domain);
        Description = ValidationHelpers.SanitizeString(Description);
        Theme = ValidationHelpers.SanitizeString(Theme);
        Timezone = ValidationHelpers.SanitizeString(Timezone);
        CreatedBy = ValidationHelpers.SanitizeString(CreatedBy);

        // Sanitize nested objects
        Logo?.Sanitize();
        
        // Sanitize collections
        if (Agents != null)
        {
            foreach (var agent in Agents)
            {
                agent.Sanitize();
            }
        }

        if (Permissions != null)
        {
            foreach (var permission in Permissions)
            {
                permission.Sanitize();
            }
        }
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate domain format
        if (!string.IsNullOrEmpty(Domain))
        {
            if (!ValidationHelpers.IsValidDomain(Domain))
            {
                throw new ValidationException("Domain format is invalid");
            }
        }

        // Validate timezone if provided
        if (!string.IsNullOrEmpty(Timezone))
        {
            if (!ValidationHelpers.IsValidTimezone(Timezone))
            {
                throw new ValidationException("Timezone format is invalid");
            }
        }

        // Validate nested objects
        if (Logo != null)
        {
            Logo.Validate();
        }

        // Validate collections
        if (Agents != null)
        {
            foreach (var agent in Agents)
            {
                agent.Validate();
            }
        }

        if (Permissions != null)
        {
            foreach (var permission in Permissions)
            {
                permission.Validate();
            }
        }
    }
    public override void ValidateAndSanitize()
    {
        Validate();
        Sanitize();
    }

    /// <summary>
    /// Validates and sanitizes a tenant ID
    /// </summary>
    /// <param name="tenantId">The raw tenant ID to validate and sanitize</param>
    /// <returns>The sanitized tenant ID</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ValidationException("Tenant ID is required");

        // Sanitize the tenant ID
        var sanitizedId = ValidationHelpers.SanitizeString(tenantId);
        
        // Validate the tenant ID format using the same pattern as the Id property
        if (!ValidationHelpers.IsValidPattern(sanitizedId, ValidationHelpers.Patterns.SafeId))
            throw new ValidationException("Invalid tenant ID format");

        return sanitizedId;
    }
    public static string SanitizeAndValidateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ValidationException("Domain is required");

        // Sanitize the tenant ID
        var sanitizedDomain = ValidationHelpers.SanitizeString(domain);
        
        // Validate the tenant ID format using the same pattern as the Id property
        if (!ValidationHelpers.IsValidPattern(sanitizedDomain, ValidationHelpers.Patterns.SafeDomain))
            throw new ValidationException("Invalid Domain format");

        return sanitizedDomain;
    }

}

public class Logo : ModelValidatorBase
{
    [BsonElement("url")]
    [StringLength(500, ErrorMessage = "Logo URL cannot exceed 500 characters")]
    [RegularExpression(@"^https?://[^\s/$.?#].[^\s]*$", ErrorMessage = "Logo URL format is invalid")]
    public string? Url { get; set; }

    [BsonElement("img_base64")]
    [StringLength(1000000, ErrorMessage = "Base64 image data is too large")]
    [RegularExpression(@"^[A-Za-z0-9+/]*={0,2}$", ErrorMessage = "Base64 image data format is invalid")]
    public string? ImgBase64 { get; set; }

    [BsonElement("width")]
    [Range(1, 10000, ErrorMessage = "Width must be between 1 and 10000 pixels")]
    public required int Width { get; set; }

    [BsonElement("height")]
    [Range(1, 10000, ErrorMessage = "Height must be between 1 and 10000 pixels")]
    public required int Height { get; set; }

    public override void Sanitize()
    {
        Url = ValidationHelpers.SanitizeUrl(Url);
        ImgBase64 = ValidationHelpers.SanitizeBase64(ImgBase64);
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate that either URL or Base64 is provided, but not both
        if (string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(ImgBase64))
        {
            throw new ValidationException("Either URL or Base64 image data must be provided");
        }

        if (!string.IsNullOrEmpty(Url) && !string.IsNullOrEmpty(ImgBase64))
        {
            throw new ValidationException("Cannot provide both URL and Base64 image data");
        }

        // Validate URL format if provided
        if (!string.IsNullOrEmpty(Url))
        {
            if (!ValidationHelpers.IsValidUrl(Url))
            {
                throw new ValidationException("Logo URL format is invalid");
            }
        }

        // Validate Base64 format if provided
        if (!string.IsNullOrEmpty(ImgBase64))
        {
            if (!ValidationHelpers.IsValidBase64(ImgBase64))
            {
                throw new ValidationException("Base64 image data format is invalid");
            }
        }
    }
}

public class Flow : ModelValidatorBase
{
    [BsonElement("name")]
    [Required(ErrorMessage = "Flow name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Flow name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@-]+$", ErrorMessage = "Flow name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("is_active")]
    public required bool IsActive { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")]
    [Required(ErrorMessage = "Created by is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Created by contains invalid characters")]
    public required string CreatedBy { get; set; }

    public override void Sanitize()
    {
        Name = ValidationHelpers.SanitizeString(Name);
        CreatedBy = ValidationHelpers.SanitizeString(CreatedBy);
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate that created_at is not in the future
        if (CreatedAt > DateTime.UtcNow)
        {
            throw new ValidationException("Created date cannot be in the future");
        }

        // Validate that updated_at is not before created_at
        if (UpdatedAt.HasValue && UpdatedAt.Value < CreatedAt)
        {
            throw new ValidationException("Updated date cannot be before created date");
        }
    }
}
