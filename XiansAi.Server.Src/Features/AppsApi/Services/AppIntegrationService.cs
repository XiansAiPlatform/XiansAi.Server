using System.ComponentModel.DataAnnotations;
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
    private readonly IConfiguration _configuration;

    public AppIntegrationService(
        IAppIntegrationRepository repository,
        ILogger<AppIntegrationService> logger,
        IConfiguration configuration)
    {
        _repository = repository;
        _logger = logger;
        _configuration = configuration;
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

    /// <summary>
    /// Gets the base URL for generating webhook URLs
    /// </summary>
    private string GetBaseUrl()
    {
        // Try to get from configuration
        var baseUrl = _configuration["AppsApi:BaseUrl"] 
            ?? _configuration["Application:BaseUrl"]
            ?? _configuration["ASPNETCORE_URLS"]?.Split(';').FirstOrDefault()
            ?? "http://localhost:5001";

        return baseUrl.TrimEnd('/');
    }

    private string GenerateWebhookPath(string platformId, string integrationId)
    {
        // Generate relative webhook path based on platform
        return platformId.ToLowerInvariant() switch
        {
            "slack" => $"/api/apps/slack/events/{integrationId}",
            "msteams" => $"/api/apps/msteams/messaging/{integrationId}",
            _ => $"/api/apps/{platformId}/events/{integrationId}"
        };
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

            var baseUrl = GetBaseUrl();
            var responses = integrations
                .Select(i => AppIntegrationResponse.FromEntity(i, baseUrl))
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

            var baseUrl = GetBaseUrl();
            var response = AppIntegrationResponse.FromEntity(integration, baseUrl);

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
                MappingConfig = request.MappingConfig ?? new AppIntegrationMappingConfig(),
                IsEnabled = request.IsEnabled,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

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
            var webhookPath = GenerateWebhookPath(integration.PlatformId, id);
            integration.Configuration["outgoingWebhookUrl"] = webhookPath;
            await _repository.UpdateAsync(integration);

            var baseUrl = GetBaseUrl();
            var response = AppIntegrationResponse.FromEntity(integration, baseUrl);

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

            existing.UpdatedBy = updatedBy;

            // Ensure outgoingWebhookUrl is set (for backward compatibility with existing integrations)
            if (!existing.Configuration.ContainsKey("outgoingWebhookUrl"))
            {
                var webhookPath = GenerateWebhookPath(existing.PlatformId, existing.Id);
                existing.Configuration["outgoingWebhookUrl"] = webhookPath;
            }

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

            var baseUrl = GetBaseUrl();
            var response = AppIntegrationResponse.FromEntity(existing, baseUrl);

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
                var baseUrl = GetBaseUrl();
                return ServiceResult<AppIntegrationResponse>.Success(
                    AppIntegrationResponse.FromEntity(existing, baseUrl));
            }

            existing.IsEnabled = true;
            existing.UpdatedBy = updatedBy;

            var success = await _repository.UpdateAsync(id, existing);

            if (!success)
            {
                return ServiceResult<AppIntegrationResponse>.InternalServerError(
                    "Failed to enable integration");
            }

            var response = AppIntegrationResponse.FromEntity(existing, GetBaseUrl());

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
                var baseUrl = GetBaseUrl();
                return ServiceResult<AppIntegrationResponse>.Success(
                    AppIntegrationResponse.FromEntity(existing, baseUrl));
            }

            existing.IsEnabled = false;
            existing.UpdatedBy = updatedBy;

            var success = await _repository.UpdateAsync(id, existing);

            if (!success)
            {
                return ServiceResult<AppIntegrationResponse>.InternalServerError(
                    "Failed to disable integration");
            }

            var response = AppIntegrationResponse.FromEntity(existing, GetBaseUrl());

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
}
