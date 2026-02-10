using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Data.Models.Validation;

namespace Features.AppsApi.Models;

/// <summary>
/// Represents an app integration that connects an external platform (Slack, Teams, etc.) 
/// to an agent activation for bidirectional messaging.
/// </summary>
[BsonIgnoreExtraElements]
public class AppIntegration : ModelValidatorBase<AppIntegration>
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "TenantId must be between 1 and 50 characters")]
    [Required(ErrorMessage = "TenantId is required")]
    public required string TenantId { get; set; }

    [BsonElement("platform_id")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "PlatformId must be between 1 and 50 characters")]
    [Required(ErrorMessage = "PlatformId is required")]
    public required string PlatformId { get; set; }

    [BsonElement("name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 100 characters")]
    [Required(ErrorMessage = "Name is required")]
    public required string Name { get; set; }

    [BsonElement("description")]
    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters")]
    public string? Description { get; set; }

    [BsonElement("agent_name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "AgentName must be between 1 and 100 characters")]
    [Required(ErrorMessage = "AgentName is required")]
    public required string AgentName { get; set; }

    [BsonElement("activation_name")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "ActivationName must be between 1 and 100 characters")]
    [Required(ErrorMessage = "ActivationName is required")]
    public required string ActivationName { get; set; }

    /// <summary>
    /// Computed workflow ID in format: {tenantId}:{agentName}:Supervisor Workflow:{activationName}
    /// </summary>
    [BsonElement("workflow_id")]
    public string WorkflowId { get; set; } = null!;

    /// <summary>
    /// Platform-specific configuration stored as key-value pairs (NON-SENSITIVE values only).
    /// For Slack: { "appId": "...", "teamId": "..." }
    /// For Teams: { "appId": "...", "appTenantId": "...", "serviceUrl": "..." }
    /// Note: Sensitive values (passwords, tokens, secrets) are stored encrypted in the Secrets field
    /// </summary>
    [BsonElement("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Encrypted secrets for this integration (stored as encrypted JSON in database).
    /// Contains sensitive values like passwords, tokens, signing secrets, webhook URLs with embedded tokens.
    /// Decrypted and populated at runtime by the repository layer.
    /// </summary>
    [BsonElement("secrets_encrypted")]
    public string? SecretsEncrypted { get; set; }

    /// <summary>
    /// Decrypted secrets (NOT stored in database - populated at runtime).
    /// Use this property to access/modify secrets in application code.
    /// </summary>
    [BsonIgnore]
    public AppIntegrationSecrets Secrets { get; set; } = new();

    /// <summary>
    /// Configuration for mapping platform-specific identifiers to XiansAi concepts
    /// </summary>
    [BsonElement("mapping_config")]
    public AppIntegrationMappingConfig MappingConfig { get; set; } = new();

    [BsonElement("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("created_by")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "CreatedBy must be between 1 and 100 characters")]
    [Required(ErrorMessage = "CreatedBy is required")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_by")]
    [StringLength(100, ErrorMessage = "UpdatedBy must not exceed 100 characters")]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Generates the workflow ID based on tenant, agent, and activation
    /// </summary>
    public void GenerateWorkflowId()
    {
        WorkflowId = $"{TenantId}:{AgentName}:Supervisor Workflow:{ActivationName}";
    }

    public override AppIntegration SanitizeAndReturn()
    {
        var sanitized = new AppIntegration
        {
            Id = this.Id,
            TenantId = ValidationHelpers.SanitizeString(this.TenantId),
            PlatformId = ValidationHelpers.SanitizeString(this.PlatformId)?.ToLowerInvariant() ?? string.Empty,
            Name = ValidationHelpers.SanitizeString(this.Name),
            Description = ValidationHelpers.SanitizeString(this.Description),
            AgentName = ValidationHelpers.SanitizeString(this.AgentName),
            ActivationName = ValidationHelpers.SanitizeString(this.ActivationName),
            WorkflowId = this.WorkflowId,
            Configuration = this.Configuration ?? new Dictionary<string, object>(),
            Secrets = this.Secrets, // Preserve secrets during sanitization
            SecretsEncrypted = this.SecretsEncrypted, // Preserve encrypted secrets
            MappingConfig = this.MappingConfig ?? new AppIntegrationMappingConfig(),
            IsEnabled = this.IsEnabled,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            CreatedBy = ValidationHelpers.SanitizeString(this.CreatedBy),
            UpdatedBy = ValidationHelpers.SanitizeString(this.UpdatedBy)
        };

        sanitized.GenerateWorkflowId();
        return sanitized;
    }

    public override AppIntegration SanitizeAndValidate()
    {
        var sanitized = this.SanitizeAndReturn();
        sanitized.Validate();
        return sanitized;
    }

    public override void Validate()
    {
        base.Validate();

        // Validate platform ID is supported
        var supportedPlatforms = new[] { "slack", "msteams", "outlook", "webhook" };
        if (!supportedPlatforms.Contains(PlatformId.ToLowerInvariant()))
        {
            throw new ValidationException($"Unsupported platform: {PlatformId}. Supported platforms: {string.Join(", ", supportedPlatforms)}");
        }

        // Validate dates
        if (!ValidationHelpers.IsValidDate(CreatedAt))
            throw new ValidationException("Creation date is invalid");

        if (CreatedAt > DateTime.UtcNow)
            throw new ValidationException("Creation date cannot be in the future");

        // Validate mapping config
        MappingConfig?.Validate();
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}

/// <summary>
/// Configuration for mapping platform-specific identifiers to XiansAi concepts
/// </summary>
[BsonIgnoreExtraElements]
public class AppIntegrationMappingConfig
{
    /// <summary>
    /// How to determine participantId from platform message.
    /// Options: "userId", "userEmail", "channelId", "threadId", "custom"
    /// </summary>
    [BsonElement("participant_id_source")]
    [JsonPropertyName("participantIdSource")]
    public string ParticipantIdSource { get; set; } = "userId";

    /// <summary>
    /// Custom field path for participant ID when source is "custom"
    /// </summary>
    [BsonElement("participant_id_custom_field")]
    [JsonPropertyName("participantIdCustomField")]
    public string? ParticipantIdCustomField { get; set; }

    /// <summary>
    /// How to determine scope/topic from platform message.
    /// Options: "channelId", "channelName", "teamId", "threadId", "subject", "custom", null (no scope)
    /// </summary>
    [BsonElement("scope_source")]
    [JsonPropertyName("scopeSource")]
    public string? ScopeSource { get; set; }

    /// <summary>
    /// Custom field path for scope when source is "custom"
    /// </summary>
    [BsonElement("scope_custom_field")]
    [JsonPropertyName("scopeCustomField")]
    public string? ScopeCustomField { get; set; }

    /// <summary>
    /// Default participant ID to use when extraction fails
    /// </summary>
    [BsonElement("default_participant_id")]
    [JsonPropertyName("defaultParticipantId")]
    public string? DefaultParticipantId { get; set; }

    /// <summary>
    /// Default scope to use when extraction fails or scopeSource is null
    /// </summary>
    [BsonElement("default_scope")]
    [JsonPropertyName("defaultScope")]
    public string? DefaultScope { get; set; }

    public void Validate()
    {
        var validSources = new[] { "userId", "userEmail", "channelId", "threadId", "custom" };
        if (!string.IsNullOrEmpty(ParticipantIdSource) && !validSources.Contains(ParticipantIdSource))
        {
            throw new ValidationException($"Invalid participantIdSource: {ParticipantIdSource}. Valid options: {string.Join(", ", validSources)}");
        }

        if (ParticipantIdSource == "custom" && string.IsNullOrEmpty(ParticipantIdCustomField))
        {
            throw new ValidationException("participantIdCustomField is required when participantIdSource is 'custom'");
        }

        var validScopeSources = new[] { "channelId", "channelName", "teamId", "threadId", "subject", "custom" };
        if (!string.IsNullOrEmpty(ScopeSource) && !validScopeSources.Contains(ScopeSource))
        {
            throw new ValidationException($"Invalid scopeSource: {ScopeSource}. Valid options: {string.Join(", ", validScopeSources)}, or null");
        }

        if (ScopeSource == "custom" && string.IsNullOrEmpty(ScopeCustomField))
        {
            throw new ValidationException("scopeCustomField is required when scopeSource is 'custom'");
        }
    }
}

/// <summary>
/// DTO for creating a new app integration
/// </summary>
public class CreateAppIntegrationRequest
{
    [Required(ErrorMessage = "PlatformId is required")]
    [JsonPropertyName("platformId")]
    public required string PlatformId { get; set; }

    [Required(ErrorMessage = "Name is required")]
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "AgentName is required")]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [Required(ErrorMessage = "ActivationName is required")]
    [JsonPropertyName("activationName")]
    public required string ActivationName { get; set; }

    [JsonPropertyName("configuration")]
    public Dictionary<string, object>? Configuration { get; set; }

    /// <summary>
    /// Secrets for this integration (will be encrypted at rest)
    /// </summary>
    [JsonPropertyName("secrets")]
    public AppIntegrationSecretsRequest? Secrets { get; set; }

    [JsonPropertyName("mappingConfig")]
    public AppIntegrationMappingConfig? MappingConfig { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// DTO for providing secrets when creating/updating integrations
/// </summary>
public class AppIntegrationSecretsRequest
{
    [JsonPropertyName("slackSigningSecret")]
    public string? SlackSigningSecret { get; set; }

    [JsonPropertyName("slackBotToken")]
    public string? SlackBotToken { get; set; }

    [JsonPropertyName("slackIncomingWebhookUrl")]
    public string? SlackIncomingWebhookUrl { get; set; }

    [JsonPropertyName("teamsAppPassword")]
    public string? TeamsAppPassword { get; set; }

    [JsonPropertyName("outlookClientSecret")]
    public string? OutlookClientSecret { get; set; }

    [JsonPropertyName("genericWebhookSecret")]
    public string? GenericWebhookSecret { get; set; }

    /// <summary>
    /// Converts request DTO to domain model
    /// </summary>
    public AppIntegrationSecrets ToSecrets(string webhookSecret)
    {
        return new AppIntegrationSecrets
        {
            WebhookSecret = webhookSecret,
            SlackSigningSecret = SlackSigningSecret,
            SlackBotToken = SlackBotToken,
            SlackIncomingWebhookUrl = SlackIncomingWebhookUrl,
            TeamsAppPassword = TeamsAppPassword,
            OutlookClientSecret = OutlookClientSecret,
            GenericWebhookSecret = GenericWebhookSecret
        };
    }
}

/// <summary>
/// DTO for updating an existing app integration
/// </summary>
public class UpdateAppIntegrationRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("configuration")]
    public Dictionary<string, object>? Configuration { get; set; }

    /// <summary>
    /// Secrets to update (will be encrypted at rest)
    /// </summary>
    [JsonPropertyName("secrets")]
    public AppIntegrationSecretsRequest? Secrets { get; set; }

    [JsonPropertyName("mappingConfig")]
    public AppIntegrationMappingConfig? MappingConfig { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; set; }
}

/// <summary>
/// Response DTO for app integration that includes generated webhook URL
/// </summary>
public class AppIntegrationResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }

    [JsonPropertyName("platformId")]
    public required string PlatformId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [JsonPropertyName("activationName")]
    public required string ActivationName { get; set; }

    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; set; }

    /// <summary>
    /// The webhook URL path (relative) that should be configured in the external platform.
    /// Append this to your server's base URL.
    /// Format: /api/apps/{platformId}/events/{integrationId}
    /// </summary>
    [JsonPropertyName("webhookUrl")]
    public required string WebhookUrl { get; set; }

    /// <summary>
    /// Platform-specific configuration (non-sensitive values only)
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Secrets with sensitive values masked (first 4 and last 4 characters shown)
    /// </summary>
    [JsonPropertyName("secrets")]
    public AppIntegrationSecrets? Secrets { get; set; }

    [JsonPropertyName("mappingConfig")]
    public AppIntegrationMappingConfig MappingConfig { get; set; } = new();

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("createdBy")]
    public required string CreatedBy { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Creates a response DTO from an AppIntegration entity
    /// </summary>
    /// <param name="entity">The integration entity</param>
    /// <param name="maskWebhookUrl">If true, masks the webhook secret in the URL (for Get endpoints). If false, shows full URL (for Create endpoint).</param>
    public static AppIntegrationResponse FromEntity(AppIntegration entity, bool maskWebhookUrl = true)
    {
        // Filter out redundant outgoingWebhookUrl from configuration (it's in webhookUrl field)
        var cleanConfig = entity.Configuration
            .Where(kvp => kvp.Key != "outgoingWebhookUrl")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Generate webhook URL with optional masking
        var webhookUrl = GenerateWebhookUrl(entity.PlatformId, entity.Id, entity.Secrets?.WebhookSecret);
        if (maskWebhookUrl && !string.IsNullOrEmpty(entity.Secrets?.WebhookSecret))
        {
            webhookUrl = MaskWebhookUrlSecret(webhookUrl, entity.Secrets.WebhookSecret);
        }

        return new AppIntegrationResponse
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            PlatformId = entity.PlatformId,
            Name = entity.Name,
            Description = entity.Description,
            AgentName = entity.AgentName,
            ActivationName = entity.ActivationName,
            WorkflowId = entity.WorkflowId,
            WebhookUrl = webhookUrl,
            Configuration = cleanConfig, // Filtered configuration without redundant fields
            Secrets = entity.Secrets?.Mask(), // Mask secrets for API response
            MappingConfig = entity.MappingConfig,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            CreatedBy = entity.CreatedBy,
            UpdatedBy = entity.UpdatedBy
        };
    }

    /// <summary>
    /// Masks the webhook secret in a webhook URL
    /// </summary>
    private static string MaskWebhookUrlSecret(string webhookUrl, string webhookSecret)
    {
        if (string.IsNullOrEmpty(webhookSecret) || webhookSecret.Length <= 8)
            return webhookUrl;

        var maskedSecret = webhookSecret[..4] + "****" + webhookSecret[^4..];
        return webhookUrl.Replace(webhookSecret, maskedSecret);
    }

    /// <summary>
    /// Generates the webhook URL path for external platforms to call.
    /// Returns a relative URL path that should be appended to your server's base URL.
    /// Includes webhook secret in URL for defense-in-depth security.
    /// </summary>
    private static string GenerateWebhookUrl(string platformId, string integrationId, string? webhookSecret)
    {
        // Return relative URL path - client can prepend their own base URL
        // Include webhook secret in URL for additional security layer
        if (string.IsNullOrEmpty(webhookSecret))
        {
            // Fallback for backward compatibility (shouldn't happen with new integrations)
            return platformId.ToLowerInvariant() switch
            {
                "slack" => $"/api/apps/slack/events/{integrationId}",
                "msteams" => $"/api/apps/msteams/events/{integrationId}",
                "webhook" => $"/api/apps/webhook/events/{integrationId}",
                "outlook" => $"/api/apps/outlook/events/{integrationId}",
                _ => throw new InvalidOperationException($"Unsupported platform: {platformId}")
            };
        }

        return platformId.ToLowerInvariant() switch
        {
            "slack" => $"/api/apps/slack/events/{integrationId}/{webhookSecret}",
            "msteams" => $"/api/apps/msteams/events/{integrationId}/{webhookSecret}",
            "webhook" => $"/api/apps/webhook/events/{integrationId}/{webhookSecret}",
            "outlook" => $"/api/apps/outlook/events/{integrationId}/{webhookSecret}",
            _ => throw new InvalidOperationException($"Unsupported platform: {platformId}")
        };
    }

    /// <summary>
    /// Masks sensitive values in the configuration for display
    /// </summary>
    private static Dictionary<string, object> MaskSensitiveConfiguration(Dictionary<string, object> config)
    {
        var sensitiveKeys = new[] { "token", "secret", "password", "key", "webhook" };
        var nonSensitiveKeys = new[] { "outgoingwebhookurl" }; // Our webhook endpoint is not sensitive
        var masked = new Dictionary<string, object>();

        foreach (var kvp in config)
        {
            var keyLower = kvp.Key.ToLowerInvariant();
            
            // Check if explicitly non-sensitive
            var isNonSensitive = nonSensitiveKeys.Any(nsk => keyLower.Contains(nsk));
            
            // Check if contains sensitive keywords
            var isSensitive = !isNonSensitive && sensitiveKeys.Any(sk => keyLower.Contains(sk));

            if (isSensitive)
            {
                var valueStr = kvp.Value.ToString() ?? "";
                if (valueStr.Length > 8)
                {
                    masked[kvp.Key] = valueStr[..4] + "****" + valueStr[^4..];
                }
                else if (valueStr.Length > 0)
                {
                    masked[kvp.Key] = "****";
                }
                else
                {
                    masked[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                masked[kvp.Key] = kvp.Value;
            }
        }

        return masked;
    }
}

/// <summary>
/// Platform-specific configuration requirements
/// </summary>
public static class PlatformConfigurationRequirements
{
    public static readonly Dictionary<string, string[]> RequiredFields = new()
    {
        ["slack"] = new[] { "signingSecret" },  // incomingWebhookUrl is optional, used for sending
        ["msteams"] = new[] { "appId", "appPassword" },
        ["outlook"] = new[] { "clientId", "clientSecret", "tenantId" },
        ["webhook"] = Array.Empty<string>()  // Generic webhook has no required fields
    };

    public static readonly Dictionary<string, string[]> OptionalFields = new()
    {
        ["slack"] = new[] { "appId", "teamId" },
        ["msteams"] = new[] { "serviceUrl", "appTenantId" },
        ["outlook"] = new[] { "userEmail", "tenantId" },
        ["webhook"] = new[] { "headers" }
    };

    public static void ValidateConfiguration(string platformId, Dictionary<string, object> configuration)
    {
        var platform = platformId.ToLowerInvariant();
        
        if (!RequiredFields.TryGetValue(platform, out var required))
        {
            throw new ValidationException($"Unknown platform: {platformId}");
        }

        var missingFields = required
            .Where(field => !configuration.ContainsKey(field) || configuration[field] == null)
            .ToList();

        if (missingFields.Count > 0)
        {
            throw new ValidationException(
                $"Missing required configuration fields for {platformId}: {string.Join(", ", missingFields)}");
        }
    }
}
