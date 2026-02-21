using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

[BsonIgnoreExtraElements]
public class AgentActivation : ModelValidatorBase<AgentActivation>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Activation name must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Activation name contains invalid characters")]
    public required string Name { get; set; }

    [BsonElement("agent_name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "AgentName must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Agent name contains invalid characters")]
    [Required(ErrorMessage = "AgentName is required")]
    public required string AgentName { get; set; }

    [BsonElement("description")]
    [StringLength(1000, ErrorMessage = "Description must not exceed 1000 characters")]
    public string? Description { get; set; }

    [BsonElement("participant_id")]
    [StringLength(100, ErrorMessage = "ParticipantId must not exceed 100 characters")]
    public string? ParticipantId { get; set; }

    [BsonElement("created_by")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Created by must be between 1 and 100 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\s._@|+\-:/\\,#=]+$", ErrorMessage = "Created by contains invalid characters")]
    [Required(ErrorMessage = "Created by is required")]
    public required string CreatedBy { get; set; }

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("tenant_id")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "TenantId must be between 1 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9._@|+\-:/\\,#=]+$", ErrorMessage = "TenantId contains invalid characters")]
    [Required(ErrorMessage = "TenantId is required")]
    public required string TenantId { get; set; }

    [BsonElement("workflow_configuration")]
    public ActivationWorkflowConfiguration? WorkflowConfiguration { get; set; }

    [BsonElement("workflow_ids")]
    public List<string> WorkflowIds { get; set; } = new();

    /// <summary>
    /// Stored field indicating whether the activation is currently active.
    /// Supports multiple activate/deactivate cycles. When null (legacy documents),
    /// IsActive falls back to inferring from ActivatedAt and DeactivatedAt.
    /// </summary>
    [BsonElement("active")]
    public bool? Active { get; set; }

    /// <summary>
    /// Computed property indicating if activation is currently active.
    /// Uses the Active field when set; falls back to ActivatedAt/DeactivatedAt for legacy documents.
    /// </summary>
    [BsonIgnore]
    public bool IsActive => Active ?? (ActivatedAt.HasValue && !DeactivatedAt.HasValue);

    [BsonElement("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [BsonElement("deactivated_at")]
    public DateTime? DeactivatedAt { get; set; }

    public override AgentActivation SanitizeAndReturn()
    {
        var sanitized = new AgentActivation
        {
            Id = this.Id,
            Name = ValidationHelpers.SanitizeString(this.Name),
            AgentName = ValidationHelpers.SanitizeString(this.AgentName),
            Description = ValidationHelpers.SanitizeString(this.Description),
            ParticipantId = ValidationHelpers.SanitizeString(this.ParticipantId),
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy),
            CreatedAt = this.CreatedAt,
            TenantId = ValidationHelpers.SanitizeString(this.TenantId),
            WorkflowConfiguration = this.WorkflowConfiguration,
            WorkflowIds = this.WorkflowIds ?? new List<string>(),
            Active = this.Active,
            ActivatedAt = this.ActivatedAt,
            DeactivatedAt = this.DeactivatedAt
        };

        return sanitized;
    }

    public override AgentActivation SanitizeAndValidate()
    {
        var sanitized = this.SanitizeAndReturn();
        sanitized.Validate();
        return sanitized;
    }

    public override void Validate()
    {
        // Call base validation (Data Annotations)
        base.Validate();

        // Validate dates
        if (!ValidationHelpers.IsValidDate(CreatedAt))
            throw new ValidationException("Creation date is invalid");

        if (CreatedAt > DateTime.UtcNow)
            throw new ValidationException("Creation date cannot be in the future");

        if (ActivatedAt.HasValue && !ValidationHelpers.IsValidDate(ActivatedAt.Value))
            throw new ValidationException("Activation date is invalid");

        if (DeactivatedAt.HasValue && !ValidationHelpers.IsValidDate(DeactivatedAt.Value))
            throw new ValidationException("Deactivation date is invalid");

        // Validate workflow configuration if provided
        if (WorkflowConfiguration != null)
        {
            WorkflowConfiguration.Validate();
        }
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
