using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class Tenant : ModelValidatorBase<Tenant>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [Required(ErrorMessage = "ID is required")]
    public required string Id { get; set; }

    [BsonElement("tenant_id")]
    [Required(ErrorMessage = "Tenant ID is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Tenant ID contains invalid characters")]
    public required string TenantId { get; set; }

    [BsonElement("name")]
    [Required(ErrorMessage = "Tenant name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Tenant name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Tenant name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("domain")]
    [StringLength(100, ErrorMessage = "Domain cannot exceed 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._\-+:|=#]+(\.[a-zA-Z]{2,})$", ErrorMessage = "Domain format is invalid")]
    public string? Domain { get; set; }

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
    [RegularExpression(@"^[a-zA-Z0-9/._\-+:]+$", ErrorMessage = "Timezone contains invalid characters")]
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
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Created by contains invalid characters")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("enabled")]
    public bool Enabled { get; set; } = true;

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
        public override Tenant SanitizeAndReturn()
    {
        // Create a new tenant with sanitized data
        var sanitizedTenant = new Tenant
        {
            Id = this.Id,
            TenantId = ValidationHelpers.SanitizeString(this.TenantId),
            Name = ValidationHelpers.SanitizeString(this.Name),
            Domain = ValidationHelpers.SanitizeString(this.Domain),
            Description = ValidationHelpers.SanitizeString(this.Description),
            Theme = ValidationHelpers.SanitizeString(this.Theme),
            Timezone = ValidationHelpers.SanitizeString(this.Timezone),
            CreatedAt = this.CreatedAt,
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy),
            UpdatedAt = this.UpdatedAt,
            Enabled = this.Enabled,
            Logo = this.Logo,
            Agents = this.Agents,
            Permissions = this.Permissions
        };

        return sanitizedTenant;
    }
    public override Tenant SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedTenant = this.SanitizeAndReturn();

        // Then validate
        sanitizedTenant.Validate();

        return sanitizedTenant;
    }
    /// <summary>
    /// Validates and sanitizes a  ID
    /// </summary>
    /// <param name="id">The raw  ID to validate and sanitize</param>
    /// <returns>The sanitized tenant ID</returns>
    /// <exception cref="ValidationException">Thrown when validation fails</exception>
    public static string SanitizeAndValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ValidationException("ID is required");

        // Sanitize the tenant ID
        var sanitizedId = ValidationHelpers.SanitizeString(id);

        // Validate the tenant ID format using the same pattern as the Id property
        if (!ValidationHelpers.IsValidPattern(sanitizedId, ValidationHelpers.Patterns.SafeId))
            throw new ValidationException($"Invalid tenant ID format --{id}--");

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
            throw new ValidationException($"Invalid Domain format {domain}. Expected format: {ValidationHelpers.Patterns.SafeDomain}");

        return sanitizedDomain;
    }

    public static string SanitizeAndValidateTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ValidationException("Tenant ID is required");

        var sanitizedTenantId = ValidationHelpers.SanitizeString(tenantId);
        if (!ValidationHelpers.IsValidPattern(sanitizedTenantId, ValidationHelpers.Patterns.SafeTenantId))
            throw new ValidationException($"Invalid Tenant ID format {tenantId}. Expected format: {ValidationHelpers.Patterns.SafeTenantId}");

        return sanitizedTenantId;
    }
}

public class Logo : ModelValidatorBase<Logo>
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
    [Range(1, 10000, ErrorMessage = "Width must be between 1 and 10000")]
    public required int Width { get; set; }

    [BsonElement("height")]
     [Range(1, 10000, ErrorMessage = "Height must be between 1 and 10000")]
    public required int Height { get; set; }
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
        public override Logo SanitizeAndReturn()
    {
        // Create a new logo with sanitized data
        var sanitizedLogo = new Logo
        {
            Url = ValidationHelpers.SanitizeUrl(this.Url),
            ImgBase64 = ValidationHelpers.SanitizeBase64(this.ImgBase64),
            Width = this.Width,
            Height = this.Height
        };

        return sanitizedLogo;
    }

    public override Logo SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedLogo = this.SanitizeAndReturn();

        // Then validate
        sanitizedLogo.Validate();

        return sanitizedLogo;
    }
}

public class Flow : ModelValidatorBase<Flow>
{
    [BsonElement("name")]
     [Required(ErrorMessage = "Flow name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Flow name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Flow name contains invalid characters")]
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
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Created by contains invalid characters")]
    public required string CreatedBy { get; set; }

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
    public override Flow SanitizeAndReturn()
    {
        // Create a new flow with sanitized data
        var sanitizedFlow = new Flow
        {
            Name = ValidationHelpers.SanitizeString(this.Name),
            IsActive = this.IsActive,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy)
        };

        return sanitizedFlow;
    }

    public override Flow SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedFlow = this.SanitizeAndReturn();

        // Then validate
        sanitizedFlow.Validate();

        return sanitizedFlow;
    }
}
