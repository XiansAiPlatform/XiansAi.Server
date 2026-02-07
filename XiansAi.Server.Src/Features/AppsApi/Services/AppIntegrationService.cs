using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Features.AppsApi.Models;
using Features.AppsApi.Repositories;
using Shared.Utils.Services;

namespace Features.AppsApi.Services;

public interface IAppIntegrationService
{
    /// <summary>
    /// Get all integrations for a tenant
    /// </summary>
    Task<ServiceResult<List<AppIntegrationResponse>>> GetIntegrationsAsync(
        string tenantId, 
        string? platformId = null,
        string? agentName = null,
        string? activationName = null);

    /// <summary>
    /// Get an integration by ID
    /// </summary>
    Task<ServiceResult<AppIntegrationResponse>> GetIntegrationByIdAsync(string id, string tenantId);

    /// <summary>
    /// Create a new integration
    /// </summary>
    Task<ServiceResult<AppIntegrationResponse>> CreateIntegrationAsync(
        CreateAppIntegrationRequest request, 
        string tenantId, 
        string createdBy);

    /// <summary>
    /// Update an existing integration
    /// </summary>
    Task<ServiceResult<AppIntegrationResponse>> UpdateIntegrationAsync(
        string id, 
        UpdateAppIntegrationRequest request, 
        string tenantId,
        string updatedBy);

    /// <summary>
    /// Delete an integration
    /// </summary>
    Task<ServiceResult<bool>> DeleteIntegrationAsync(string id, string tenantId);

    /// <summary>
    /// Enable an integration
    /// </summary>
    Task<ServiceResult<AppIntegrationResponse>> EnableIntegrationAsync(string id, string tenantId, string updatedBy);

    /// <summary>
    /// Disable an integration
    /// </summary>
    Task<ServiceResult<AppIntegrationResponse>> DisableIntegrationAsync(string id, string tenantId, string updatedBy);

    /// <summary>
    /// Get the raw integration entity (for internal use by proxies)
    /// </summary>
    Task<AppIntegration?> GetIntegrationEntityByIdAsync(string id);

    /// <summary>
    /// Test the integration configuration (validate credentials)
    /// </summary>
    Task<ServiceResult<IntegrationTestResult>> TestIntegrationAsync(string id, string tenantId);
}

/// <summary>
/// Result of testing an integration
/// </summary>
public class IntegrationTestResult
{
    public bool IsSuccessful { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

public class AppIntegrationService : IAppIntegrationService
{
    private readonly IAppIntegrationRepository _repository;
    private readonly ILogger<AppIntegrationService> _logger;

    public AppIntegrationService(
        IAppIntegrationRepository repository,
        ILogger<AppIntegrationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Converts configuration dictionary to handle JsonElement objects from API deserialization
    /// </summary>
    private Dictionary<string, object> ConvertConfiguration(Dictionary<string, object> config)
    {
        var converted = new Dictionary<string, object>();
        
        foreach (var kvp in config)
        {
            if (kvp.Value is JsonElement jsonElement)
            {
                // Convert JsonElement to appropriate type
                converted[kvp.Key] = jsonElement.ValueKind switch
                {
                    JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                    JsonValueKind.Number => jsonElement.TryGetInt32(out var intValue) ? intValue : jsonElement.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    JsonValueKind.Object => jsonElement.GetRawText(),
                    JsonValueKind.Array => jsonElement.GetRawText(),
                    _ => kvp.Value
                };
            }
            else
            {
                converted[kvp.Key] = kvp.Value;
            }
        }
        
        return converted;
    }


    private string GenerateWebhookPath(string platformId, string integrationId, string webhookSecret)
    {
        // Generate relative webhook path based on platform (includes webhook secret for security)
        return platformId.ToLowerInvariant() switch
        {
            "slack" => $"/api/apps/slack/events/{integrationId}/{webhookSecret}",
            "msteams" => $"/api/apps/msteams/messaging/{integrationId}/{webhookSecret}",
            _ => $"/api/apps/{platformId}/events/{integrationId}/{webhookSecret}"
        };
    }

    private static string GenerateSecureRandomString(int length)
    {
        // Generate cryptographically secure random string for webhook secrets
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var randomBytes = new byte[length];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        return new string(randomBytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    public async Task<ServiceResult<List<AppIntegrationResponse>>> GetIntegrationsAsync(
        string tenantId,
        string? platformId = null,
        string? agentName = null,
        string? activationName = null)
    {
        try
        {
            _logger.LogInformation("Getting integrations for tenant {TenantId}, platform={Platform}, agent={Agent}, activation={Activation}",
                tenantId, platformId, agentName, activationName);

            List<AppIntegration> integrations;

            if (!string.IsNullOrEmpty(agentName) && !string.IsNullOrEmpty(activationName))
            {
                integrations = await _repository.GetByAgentActivationAsync(tenantId, agentName, activationName);
            }
            else if (!string.IsNullOrEmpty(platformId))
            {
                integrations = await _repository.GetByTenantAndPlatformAsync(tenantId, platformId);
            }
            else
            {
                integrations = await _repository.GetByTenantIdAsync(tenantId);
            }

            // Apply additional filters if partial criteria provided
            if (!string.IsNullOrEmpty(agentName) && string.IsNullOrEmpty(activationName))
            {
                integrations = integrations.Where(i => i.AgentName == agentName).ToList();
            }

            var responses = integrations
                .Select(i => AppIntegrationResponse.FromEntity(i))
                .ToList();

            _logger.LogInformation("Found {Count} integrations for tenant {TenantId}", responses.Count, tenantId);

            return ServiceResult<List<AppIntegrationResponse>>.Success(responses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting integrations for tenant {TenantId}", tenantId);
            return ServiceResult<List<AppIntegrationResponse>>.InternalServerError(
                "An error occurred while retrieving integrations");
        }
    }

    public async Task<ServiceResult<AppIntegrationResponse>> GetIntegrationByIdAsync(string id, string tenantId)
    {
        try
        {
            _logger.LogInformation("Getting integration {IntegrationId} for tenant {TenantId}", id, tenantId);

            var integration = await _repository.GetByIdAsync(id);

            if (integration == null)
            {
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            if (integration.TenantId != tenantId)
            {
                _logger.LogWarning("Tenant {TenantId} attempted to access integration {IntegrationId} belonging to tenant {OwnerTenant}",
                    tenantId, id, integration.TenantId);
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            var response = AppIntegrationResponse.FromEntity(integration);

            return ServiceResult<AppIntegrationResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting integration {IntegrationId}", id);
            return ServiceResult<AppIntegrationResponse>.InternalServerError(
                "An error occurred while retrieving the integration");
        }
    }

    public async Task<ServiceResult<AppIntegrationResponse>> CreateIntegrationAsync(
        CreateAppIntegrationRequest request,
        string tenantId,
        string createdBy)
    {
        try
        {
            _logger.LogInformation("Creating integration {Name} for tenant {TenantId}, platform {Platform}",
                request.Name, tenantId, request.PlatformId);

            // Check if name already exists for this agent/activation combination
            if (await _repository.ExistsByNameAsync(tenantId, request.AgentName, request.ActivationName, request.Name))
            {
                return ServiceResult<AppIntegrationResponse>.BadRequest(
                    $"An integration with name '{request.Name}' already exists for agent '{request.AgentName}' and activation '{request.ActivationName}'");
            }

            // Validate platform-specific configuration
            var configuration = ConvertConfiguration(request.Configuration ?? new Dictionary<string, object>());
            try
            {
                PlatformConfigurationRequirements.ValidateConfiguration(request.PlatformId, configuration);
            }
            catch (ValidationException ex)
            {
                return ServiceResult<AppIntegrationResponse>.BadRequest(ex.Message);
            }

            // Generate webhook secret for URL security
            var webhookSecret = GenerateSecureRandomString(32);

            // Create the integration entity
            var integration = new AppIntegration
            {
                TenantId = tenantId,
                PlatformId = request.PlatformId.ToLowerInvariant(),
                Name = request.Name,
                Description = request.Description,
                AgentName = request.AgentName,
                ActivationName = request.ActivationName,
                Configuration = configuration,
                Secrets = request.Secrets?.ToSecrets(webhookSecret) ?? new AppIntegrationSecrets { WebhookSecret = webhookSecret },
                MappingConfig = request.MappingConfig ?? new AppIntegrationMappingConfig(),
                IsEnabled = request.IsEnabled,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Migrate secrets from Configuration to Secrets for backward compatibility
            MigrateSecretsFromConfiguration(integration);

            // Validate and sanitize
            try
            {
                integration = integration.SanitizeAndValidate();
            }
            catch (ValidationException ex)
            {
                return ServiceResult<AppIntegrationResponse>.BadRequest(ex.Message);
            }

            // Create in database
            var id = await _repository.CreateAsync(integration);
            integration.Id = id;

            // Add the outgoing webhook URL to configuration for easy reference
            var webhookPath = GenerateWebhookPath(integration.PlatformId, id, webhookSecret);
            integration.Configuration["outgoingWebhookUrl"] = webhookPath;
            await _repository.UpdateAsync(id, integration);

            var response = AppIntegrationResponse.FromEntity(integration);

            _logger.LogInformation("Created integration {IntegrationId} with webhook URL {WebhookUrl}",
                id, response.WebhookUrl);

            return ServiceResult<AppIntegrationResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating integration for tenant {TenantId}", tenantId);
            return ServiceResult<AppIntegrationResponse>.InternalServerError(
                "An error occurred while creating the integration");
        }
    }

    public async Task<ServiceResult<AppIntegrationResponse>> UpdateIntegrationAsync(
        string id,
        UpdateAppIntegrationRequest request,
        string tenantId,
        string updatedBy)
    {
        try
        {
            _logger.LogInformation("Updating integration {IntegrationId} for tenant {TenantId}", id, tenantId);

            var existing = await _repository.GetByIdAsync(id);

            if (existing == null)
            {
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            if (existing.TenantId != tenantId)
            {
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            // Check name uniqueness if name is being changed
            if (!string.IsNullOrEmpty(request.Name) && request.Name != existing.Name)
            {
                if (await _repository.ExistsByNameAsync(tenantId, existing.AgentName, existing.ActivationName, request.Name, id))
                {
                    return ServiceResult<AppIntegrationResponse>.BadRequest(
                        $"An integration with name '{request.Name}' already exists for agent '{existing.AgentName}' and activation '{existing.ActivationName}'");
                }
                existing.Name = request.Name;
            }

            // Update fields if provided
            if (request.Description != null)
            {
                existing.Description = request.Description;
            }

            if (request.Configuration != null)
            {
                // Convert and merge configuration - allow partial updates
                var convertedConfig = ConvertConfiguration(request.Configuration);
                foreach (var kvp in convertedConfig)
                {
                    existing.Configuration[kvp.Key] = kvp.Value;
                }

                // Validate the merged configuration
                try
                {
                    PlatformConfigurationRequirements.ValidateConfiguration(existing.PlatformId, existing.Configuration);
                }
                catch (ValidationException ex)
                {
                    return ServiceResult<AppIntegrationResponse>.BadRequest(ex.Message);
                }
            }

            if (request.MappingConfig != null)
            {
                existing.MappingConfig = request.MappingConfig;
            }

            if (request.IsEnabled.HasValue)
            {
                existing.IsEnabled = request.IsEnabled.Value;
            }

            // Update secrets if provided
            if (request.Secrets != null)
            {
                // Preserve existing webhook secret if not being updated
                var webhookSecret = existing.Secrets?.WebhookSecret ?? GenerateSecureRandomString(32);
                
                // Merge secrets - update only provided values
                if (existing.Secrets == null)
                {
                    existing.Secrets = new AppIntegrationSecrets { WebhookSecret = webhookSecret };
                }

                if (request.Secrets.SlackSigningSecret != null)
                    existing.Secrets.SlackSigningSecret = request.Secrets.SlackSigningSecret;
                if (request.Secrets.SlackBotToken != null)
                    existing.Secrets.SlackBotToken = request.Secrets.SlackBotToken;
                if (request.Secrets.SlackIncomingWebhookUrl != null)
                    existing.Secrets.SlackIncomingWebhookUrl = request.Secrets.SlackIncomingWebhookUrl;
                if (request.Secrets.TeamsAppPassword != null)
                    existing.Secrets.TeamsAppPassword = request.Secrets.TeamsAppPassword;
                if (request.Secrets.OutlookClientSecret != null)
                    existing.Secrets.OutlookClientSecret = request.Secrets.OutlookClientSecret;
                if (request.Secrets.GenericWebhookSecret != null)
                    existing.Secrets.GenericWebhookSecret = request.Secrets.GenericWebhookSecret;
            }

            // Migrate secrets from Configuration to Secrets for backward compatibility
            MigrateSecretsFromConfiguration(existing);

            // Ensure webhook secret exists (for backward compatibility)
            if (existing.Secrets == null || string.IsNullOrEmpty(existing.Secrets.WebhookSecret))
            {
                if (existing.Secrets == null)
                    existing.Secrets = new AppIntegrationSecrets();
                existing.Secrets.WebhookSecret = GenerateSecureRandomString(32);
            }

            // Ensure outgoingWebhookUrl is set (for backward compatibility with existing integrations)
            if (!existing.Configuration.ContainsKey("outgoingWebhookUrl"))
            {
                var webhookPath = GenerateWebhookPath(existing.PlatformId, existing.Id, existing.Secrets.WebhookSecret!);
                existing.Configuration["outgoingWebhookUrl"] = webhookPath;
            }

            existing.UpdatedBy = updatedBy;

            // Validate
            try
            {
                existing = existing.SanitizeAndValidate();
            }
            catch (ValidationException ex)
            {
                return ServiceResult<AppIntegrationResponse>.BadRequest(ex.Message);
            }

            // Update in database
            var success = await _repository.UpdateAsync(id, existing);

            if (!success)
            {
                return ServiceResult<AppIntegrationResponse>.InternalServerError(
                    "Failed to update integration");
            }

            var response = AppIntegrationResponse.FromEntity(existing);

            _logger.LogInformation("Updated integration {IntegrationId}", id);

            return ServiceResult<AppIntegrationResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating integration {IntegrationId}", id);
            return ServiceResult<AppIntegrationResponse>.InternalServerError(
                "An error occurred while updating the integration");
        }
    }

    public async Task<ServiceResult<bool>> DeleteIntegrationAsync(string id, string tenantId)
    {
        try
        {
            _logger.LogInformation("Deleting integration {IntegrationId} for tenant {TenantId}", id, tenantId);

            var existing = await _repository.GetByIdAsync(id);

            if (existing == null)
            {
                return ServiceResult<bool>.NotFound("Integration not found");
            }

            if (existing.TenantId != tenantId)
            {
                return ServiceResult<bool>.NotFound("Integration not found");
            }

            var success = await _repository.DeleteAsync(id, tenantId);

            if (!success)
            {
                return ServiceResult<bool>.InternalServerError("Failed to delete integration");
            }

            _logger.LogInformation("Deleted integration {IntegrationId}", id);

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting integration {IntegrationId}", id);
            return ServiceResult<bool>.InternalServerError(
                "An error occurred while deleting the integration");
        }
    }

    public async Task<ServiceResult<AppIntegrationResponse>> EnableIntegrationAsync(
        string id, 
        string tenantId, 
        string updatedBy)
    {
        try
        {
            _logger.LogInformation("Enabling integration {IntegrationId}", id);

            var existing = await _repository.GetByIdAsync(id);

            if (existing == null || existing.TenantId != tenantId)
            {
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            if (existing.IsEnabled)
            {
                return ServiceResult<AppIntegrationResponse>.Success(
                    AppIntegrationResponse.FromEntity(existing));
            }

            existing.IsEnabled = true;
            existing.UpdatedBy = updatedBy;

            var success = await _repository.UpdateAsync(id, existing);

            if (!success)
            {
                return ServiceResult<AppIntegrationResponse>.InternalServerError(
                    "Failed to enable integration");
            }

            var response = AppIntegrationResponse.FromEntity(existing);

            _logger.LogInformation("Enabled integration {IntegrationId}", id);

            return ServiceResult<AppIntegrationResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling integration {IntegrationId}", id);
            return ServiceResult<AppIntegrationResponse>.InternalServerError(
                "An error occurred while enabling the integration");
        }
    }

    public async Task<ServiceResult<AppIntegrationResponse>> DisableIntegrationAsync(
        string id, 
        string tenantId, 
        string updatedBy)
    {
        try
        {
            _logger.LogInformation("Disabling integration {IntegrationId}", id);

            var existing = await _repository.GetByIdAsync(id);

            if (existing == null || existing.TenantId != tenantId)
            {
                return ServiceResult<AppIntegrationResponse>.NotFound("Integration not found");
            }

            if (!existing.IsEnabled)
            {
                return ServiceResult<AppIntegrationResponse>.Success(
                    AppIntegrationResponse.FromEntity(existing));
            }

            existing.IsEnabled = false;
            existing.UpdatedBy = updatedBy;

            var success = await _repository.UpdateAsync(id, existing);

            if (!success)
            {
                return ServiceResult<AppIntegrationResponse>.InternalServerError(
                    "Failed to disable integration");
            }

            var response = AppIntegrationResponse.FromEntity(existing);

            _logger.LogInformation("Disabled integration {IntegrationId}", id);

            return ServiceResult<AppIntegrationResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling integration {IntegrationId}", id);
            return ServiceResult<AppIntegrationResponse>.InternalServerError(
                "An error occurred while disabling the integration");
        }
    }

    public async Task<AppIntegration?> GetIntegrationEntityByIdAsync(string id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<ServiceResult<IntegrationTestResult>> TestIntegrationAsync(string id, string tenantId)
    {
        try
        {
            _logger.LogInformation("Testing integration {IntegrationId}", id);

            var integration = await _repository.GetByIdAsync(id);

            if (integration == null || integration.TenantId != tenantId)
            {
                return ServiceResult<IntegrationTestResult>.NotFound("Integration not found");
            }

            // Platform-specific test logic
            var result = integration.PlatformId.ToLowerInvariant() switch
            {
                "slack" => await TestSlackIntegrationAsync(integration),
                "msteams" => await TestTeamsIntegrationAsync(integration),
                "outlook" => await TestOutlookIntegrationAsync(integration),
                _ => new IntegrationTestResult
                {
                    IsSuccessful = true,
                    Message = "Configuration validation passed (no live test available for this platform)"
                }
            };

            return ServiceResult<IntegrationTestResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing integration {IntegrationId}", id);
            return ServiceResult<IntegrationTestResult>.InternalServerError(
                "An error occurred while testing the integration");
        }
    }

    private Task<IntegrationTestResult> TestSlackIntegrationAsync(AppIntegration integration)
    {
        // For Slack, we can test by sending a test message if incomingWebhookUrl is configured
        var hasWebhook = integration.Configuration.TryGetValue("incomingWebhookUrl", out var webhookUrl) 
            && !string.IsNullOrEmpty(webhookUrl?.ToString());
        var hasSigningSecret = integration.Configuration.TryGetValue("signingSecret", out var secret) 
            && !string.IsNullOrEmpty(secret?.ToString());

        var details = new Dictionary<string, object>
        {
            ["hasIncomingWebhookUrl"] = hasWebhook,
            ["hasSigningSecret"] = hasSigningSecret,
            ["hasBotToken"] = integration.Configuration.ContainsKey("botToken")
        };

        if (!hasSigningSecret)
        {
            return Task.FromResult(new IntegrationTestResult
            {
                IsSuccessful = false,
                Message = "Signing secret is required for Slack integration",
                Details = details
            });
        }

        return Task.FromResult(new IntegrationTestResult
        {
            IsSuccessful = true,
            Message = "Slack configuration is valid. Configure the webhook URL in your Slack app settings.",
            Details = details
        });
    }

    private Task<IntegrationTestResult> TestTeamsIntegrationAsync(AppIntegration integration)
    {
        var hasAppId = integration.Configuration.ContainsKey("appId");
        var hasAppPassword = integration.Configuration.ContainsKey("appPassword");

        var details = new Dictionary<string, object>
        {
            ["hasAppId"] = hasAppId,
            ["hasAppPassword"] = hasAppPassword
        };

        if (!hasAppId || !hasAppPassword)
        {
            return Task.FromResult(new IntegrationTestResult
            {
                IsSuccessful = false,
                Message = "App ID and App Password are required for Teams integration",
                Details = details
            });
        }

        return Task.FromResult(new IntegrationTestResult
        {
            IsSuccessful = true,
            Message = "Teams configuration is valid",
            Details = details
        });
    }

    private Task<IntegrationTestResult> TestOutlookIntegrationAsync(AppIntegration integration)
    {
        var hasClientId = integration.Configuration.ContainsKey("clientId");
        var hasClientSecret = integration.Configuration.ContainsKey("clientSecret");
        var hasTenantId = integration.Configuration.ContainsKey("tenantId");

        var details = new Dictionary<string, object>
        {
            ["hasClientId"] = hasClientId,
            ["hasClientSecret"] = hasClientSecret,
            ["hasTenantId"] = hasTenantId
        };

        if (!hasClientId || !hasClientSecret || !hasTenantId)
        {
            return Task.FromResult(new IntegrationTestResult
            {
                IsSuccessful = false,
                Message = "Client ID, Client Secret, and Tenant ID are required for Outlook integration",
                Details = details
            });
        }

        return Task.FromResult(new IntegrationTestResult
        {
            IsSuccessful = true,
            Message = "Outlook configuration is valid",
            Details = details
        });
    }

    /// <summary>
    /// Migrates secrets from Configuration to Secrets for backward compatibility.
    /// Removes them from Configuration after migration.
    /// </summary>
    private static void MigrateSecretsFromConfiguration(AppIntegration integration)
    {
        var configToRemove = new List<string>();

        // Migrate platform-specific secrets based on platformId
        switch (integration.PlatformId.ToLowerInvariant())
        {
            case "slack":
                // Migrate Slack secrets
                if (TryGetAndRemove(integration.Configuration, "signingSecret", out var slackSigningSecret))
                {
                    integration.Secrets.SlackSigningSecret = slackSigningSecret;
                    configToRemove.Add("signingSecret");
                }
                if (TryGetAndRemove(integration.Configuration, "botToken", out var slackBotToken))
                {
                    integration.Secrets.SlackBotToken = slackBotToken;
                    configToRemove.Add("botToken");
                }
                // Support both spellings
                if (TryGetAndRemove(integration.Configuration, "incomingWebhookUrl", out var slackWebhook) ||
                    TryGetAndRemove(integration.Configuration, "incomingWekhookUrl", out slackWebhook))
                {
                    integration.Secrets.SlackIncomingWebhookUrl = slackWebhook;
                    configToRemove.Add("incomingWebhookUrl");
                    configToRemove.Add("incomingWekhookUrl");
                }
                break;

            case "msteams":
                // Migrate Teams secrets
                if (TryGetAndRemove(integration.Configuration, "appPassword", out var teamsPassword))
                {
                    integration.Secrets.TeamsAppPassword = teamsPassword;
                    configToRemove.Add("appPassword");
                }
                break;

            case "outlook":
                // Migrate Outlook secrets
                if (TryGetAndRemove(integration.Configuration, "clientSecret", out var outlookSecret))
                {
                    integration.Secrets.OutlookClientSecret = outlookSecret;
                    configToRemove.Add("clientSecret");
                }
                break;

            case "webhook":
                // Migrate generic webhook secrets
                if (TryGetAndRemove(integration.Configuration, "secret", out var webhookSecret))
                {
                    integration.Secrets.GenericWebhookSecret = webhookSecret;
                    configToRemove.Add("secret");
                }
                break;
        }

        // Remove migrated secrets from Configuration
        foreach (var key in configToRemove.Distinct())
        {
            integration.Configuration.Remove(key);
        }
    }

    /// <summary>
    /// Tries to get a string value from configuration dictionary
    /// </summary>
    private static bool TryGetAndRemove(Dictionary<string, object> config, string key, out string? value)
    {
        if (config.TryGetValue(key, out var obj) && obj?.ToString() is string strValue && !string.IsNullOrEmpty(strValue))
        {
            value = strValue;
            return true;
        }
        value = null;
        return false;
    }
}
