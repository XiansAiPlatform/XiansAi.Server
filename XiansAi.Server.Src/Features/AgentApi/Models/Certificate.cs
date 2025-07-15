// Features/AgentApi/Models/Certificate.cs
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Features.AgentApi.Models;

[BsonIgnoreExtraElements]
public class Certificate : ModelValidatorBase<Certificate>
{
    // Regex pattern for certificate thumbprint validation (40 character hex string)
    private static readonly Regex ThumbprintPattern = new(@"^[a-fA-F0-9]{40}$", RegexOptions.Compiled);

    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("thumbprint")]
    [Required(ErrorMessage = "Certificate thumbprint is required")]
    [StringLength(40, MinimumLength = 40, ErrorMessage = "Certificate thumbprint must be exactly 40 characters")]
    [RegularExpression(@"^[a-fA-F0-9]{40}$", ErrorMessage = "Certificate thumbprint must be a 40-character hexadecimal string")]
    public string Thumbprint { get; set; } = string.Empty;

    [BsonElement("subject_name")]
    [Required(ErrorMessage = "Certificate subject name is required")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Certificate subject name must be between 1 and 500 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Certificate subject name contains invalid characters")] public string SubjectName { get; set; } = string.Empty;

    [BsonElement("tenant_id")]
    [Required(ErrorMessage = "Tenant ID is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tenant ID must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Tenant ID contains invalid characters")]
    public string TenantId { get; set; } = string.Empty;

    [BsonElement("issued_to")]
    [Required(ErrorMessage = "Issued to is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Issued to must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Issued to contains invalid characters")] public string IssuedTo { get; set; } = string.Empty;

    [BsonElement("issued_at")]
    [Required(ErrorMessage = "Issued at date is required")]
    public DateTime IssuedAt { get; set; }

    [BsonElement("expires_at")]
    [Required(ErrorMessage = "Expires at date is required")]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("is_revoked")]
    public bool IsRevoked { get; set; }

    [BsonElement("revocation_reason")]
    [StringLength(500, ErrorMessage = "Revocation reason cannot exceed 500 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Revocation reason contains invalid characters")]
    public string? RevocationReason { get; set; }

    [BsonElement("revoked_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? RevokedAt { get; set; }

    [BsonElement("created_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public override Certificate SanitizeAndReturn()
    {
        // Create a new certificate with sanitized data
        var sanitizedCertificate = new Certificate
        {
            Id = this.Id,
            Thumbprint = ValidationHelpers.SanitizeString(this.Thumbprint),
            SubjectName = ValidationHelpers.SanitizeString(this.SubjectName),
            TenantId = ValidationHelpers.SanitizeString(this.TenantId),
            IssuedTo = ValidationHelpers.SanitizeString(this.IssuedTo),
            IssuedAt = this.IssuedAt,
            ExpiresAt = this.ExpiresAt,
            IsRevoked = this.IsRevoked,
            RevocationReason = ValidationHelpers.SanitizeString(this.RevocationReason ?? string.Empty),
            RevokedAt = this.RevokedAt,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt
        };

        return sanitizedCertificate;
    }

    public override Certificate SanitizeAndValidate()
    {
        // First sanitize
        var sanitizedCertificate = this.SanitizeAndReturn();
        sanitizedCertificate.Validate();

        return sanitizedCertificate;
    }

    public override void Validate()
    {

        // Then validate
        base.Validate();

        // Validate thumbprint format
        if (!ValidationHelpers.IsValidPattern(Thumbprint, ThumbprintPattern))
            throw new ValidationException("Invalid certificate thumbprint format");

        // Validate dates
        if (!ValidationHelpers.IsValidDate(IssuedAt))
            throw new ValidationException("Certificate issued date is invalid");

        if (!ValidationHelpers.IsValidDate(ExpiresAt))
            throw new ValidationException("Certificate expiry date is invalid");

        if (IssuedAt > DateTime.UtcNow)
            throw new ValidationException("Certificate issued date cannot be in the future");

        if (ExpiresAt < IssuedAt)
            throw new ValidationException("Certificate expiry date must be after issued date");

        if (RevokedAt.HasValue)
        {
            if (!ValidationHelpers.IsValidDate(RevokedAt.Value))
                throw new ValidationException("Certificate revocation date is invalid");

            if (RevokedAt.Value < IssuedAt)
                throw new ValidationException("Certificate revocation date cannot be before issued date");
        }

        // Validate that revoked certificates have revocation reason
        if (IsRevoked && string.IsNullOrWhiteSpace(RevocationReason))
            throw new ValidationException("Revoked certificates must have a revocation reason");
        if (IsRevoked && string.IsNullOrWhiteSpace(RevocationReason?.Trim()))
            throw new ValidationException("Revoked certificates must have a revocation reason");

        // Validate that non-revoked certificates don't have revocation date
        if (!IsRevoked && RevokedAt.HasValue)
            throw new ValidationException("Non-revoked certificates cannot have a revocation date");

    }

}

public class CertificateValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<string> Errors { get; } = new();
    public void AddError(string error) => Errors.Add(error);
}