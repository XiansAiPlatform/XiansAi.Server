using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Endpoints;
using Shared.Auth;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;


/// <summary>
/// Metadata about a supported integration type
/// </summary>
public class IntegrationTypeMetadata
{
    /// <summary>
    /// Platform identifier (slack, msteams, outlook, webhook)
    /// </summary>
    public required string PlatformId { get; set; }

    /// <summary>
    /// User-friendly display name
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Description of the integration type
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Icon identifier for UI
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// List of required configuration fields
    /// </summary>
    public List<ConfigurationFieldMetadata> RequiredConfigurationFields { get; set; } = new();

    /// <summary>
    /// List of capabilities/features supported by this platform
    /// </summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>
    /// Webhook endpoint pattern for this integration type
    /// </summary>
    public string? WebhookEndpoint { get; set; }

    /// <summary>
    /// URL to platform documentation
    /// </summary>
    public string? DocumentationUrl { get; set; }
}

/// <summary>
/// Metadata about a configuration field
/// </summary>
public class ConfigurationFieldMetadata
{
    /// <summary>
    /// Field name/key
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// User-friendly display name
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Description of the field
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// Whether this field contains sensitive data (should be encrypted)
    /// </summary>
    public bool IsSecret { get; set; }
}
/// <summary>
/// AdminApi endpoints for managing app integrations (Slack, Teams, Outlook, etc.).
/// These endpoints allow creating, updating, deleting, and managing integrations
/// that connect external platforms to agent activations.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminAppIntegrationEndpoints
{
    /// <summary>
    /// Maps all AdminApi app integration endpoints.
    /// </summary>
    public static void MapAdminAppIntegrationEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        // Metadata endpoints (no tenant context required)
        var metadataGroup = adminApiGroup.MapGroup("/integrations/metadata")
            .WithTags("AdminAPI - Integration Metadata")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        metadataGroup.MapGet("/types", GetIntegrationTypes)
            .WithName("GetIntegrationMetadataTypes")
            .Produces<List<IntegrationTypeMetadata>>(StatusCodes.Status200OK)
            ;

        // Builtin webhook endpoints (stored as app integrations with platformId=builtin_webhook)
        var webhookGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/webhooks")
            .WithTags("AdminAPI - Webhooks")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        webhookGroup.MapPost("", async (
            string tenantId,
            [FromBody] CreateBuiltinWebhookRequest request,
            [FromServices] IAppIntegrationService integrationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var createdBy = tenantContext.LoggedInUser ?? "system";
            var result = await integrationService.CreateBuiltinWebhookAsync(request, tenantId, createdBy);
            return result.ToHttpResult();
        })
        .WithName("CreateWebhook")
        
        .Produces<AppIntegrationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        webhookGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string? activationName,
            [FromQuery] string? agentName,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.GetBuiltinWebhooksAsync(tenantId, activationName, agentName);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { webhooks = result.Data });
        })
        .WithName("ListWebhooks")
        
        .Produces(StatusCodes.Status200OK);

        webhookGroup.MapDelete("{integrationId}", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.DeleteBuiltinWebhookAsync(integrationId, tenantId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = "Webhook deleted successfully" });
        })
        .WithName("DeleteWebhook")
        
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // Tenant-specific integration endpoints
        var integrationGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/integrations")
            .WithTags("AdminAPI - App Integrations")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List all integrations for a tenant
        integrationGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string? platformId,
            [FromQuery] string? agentName,
            [FromQuery] string? activationName,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.GetIntegrationsAsync(
                tenantId, platformId, agentName, activationName);
            return result.ToHttpResult();
        })
        .WithName("ListAppIntegrations")
        ;

        // Get integration by ID
        integrationGroup.MapGet("/{integrationId}", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.GetIntegrationByIdAsync(integrationId, tenantId);
            return result.ToHttpResult();
        })
        .WithName("GetAppIntegration")
        ;

        // Create a new integration
        integrationGroup.MapPost("", async (
            string tenantId,
            [FromBody] CreateAppIntegrationRequest request,
            [FromServices] IAppIntegrationService integrationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var userId = tenantContext.LoggedInUser ?? "system";
            var result = await integrationService.CreateIntegrationAsync(request, tenantId, userId);
            return result.ToHttpResult();
        })
        .WithName("CreateAppIntegration")
        ;

        // Update an existing integration
        integrationGroup.MapPut("/{integrationId}", async (
            string tenantId,
            string integrationId,
            [FromBody] UpdateAppIntegrationRequest request,
            [FromServices] IAppIntegrationService integrationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var userId = tenantContext.LoggedInUser ?? "system";
            var result = await integrationService.UpdateIntegrationAsync(
                integrationId, request, tenantId, userId);
            return result.ToHttpResult();
        })
        .WithName("UpdateAppIntegration")
        ;

        // Delete an integration
        integrationGroup.MapDelete("/{integrationId}", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.DeleteIntegrationAsync(integrationId, tenantId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new { message = $"Integration '{integrationId}' deleted successfully" });
        })
        .WithName("DeleteAppIntegration")
        ;

        // Enable an integration
        integrationGroup.MapPost("/{integrationId}/enable", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var userId = tenantContext.LoggedInUser ?? "system";
            var result = await integrationService.EnableIntegrationAsync(integrationId, tenantId, userId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new
            {
                message = $"Integration '{integrationId}' enabled successfully",
                integration = result.Data
            });
        })
        .WithName("EnableAppIntegration")
        ;

        // Disable an integration
        integrationGroup.MapPost("/{integrationId}/disable", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService,
            [FromServices] ITenantContext tenantContext) =>
        {
            var userId = tenantContext.LoggedInUser ?? "system";
            var result = await integrationService.DisableIntegrationAsync(integrationId, tenantId, userId);
            if (!result.IsSuccess)
            {
                return result.ToHttpResult();
            }
            return Results.Ok(new
            {
                message = $"Integration '{integrationId}' disabled successfully",
                integration = result.Data
            });
        })
        .WithName("DisableAppIntegration")
        ;

        // Test an integration
        integrationGroup.MapPost("/{integrationId}/test", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.TestIntegrationAsync(integrationId, tenantId);
            return result.ToHttpResult();
        })
        .WithName("TestAppIntegration")
        ;

        // Get webhook URL for an integration (convenience endpoint)
        integrationGroup.MapGet("/{integrationId}/webhook-url", async (
            string tenantId,
            string integrationId,
            [FromServices] IAppIntegrationService integrationService) =>
        {
            var result = await integrationService.GetIntegrationByIdAsync(integrationId, tenantId);
            if (!result.IsSuccess || result.Data == null)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new
            {
                integrationId = result.Data.Id,
                platformId = result.Data.PlatformId,
                webhookUrl = result.Data.WebhookUrl,
                instructions = GetWebhookInstructions(result.Data.PlatformId)
            });
        })
        .WithName("GetAppIntegrationWebhookUrl")
        ;
    }

    /// <summary>
    /// Returns platform-specific webhook setup instructions
    /// </summary>
    private static object GetWebhookInstructions(string platformId)
    {
        return platformId.ToLowerInvariant() switch
        {
            "slack" => new
            {
                steps = new[]
                {
                    "1. Go to your Slack App settings at https://api.slack.com/apps",
                    "2. Navigate to 'Event Subscriptions'",
                    "3. Enable Events and paste the webhook URL in 'Request URL'",
                    "4. Subscribe to bot events: message.channels, message.im, app_mention",
                    "5. Save changes and reinstall the app to your workspace"
                },
                documentation = "https://api.slack.com/events-api"
            },
            "msteams" => new
            {
                steps = new[]
                {
                    "1. Go to Azure Bot Service or Bot Framework portal",
                    "2. Configure the messaging endpoint with this webhook URL",
                    "3. Ensure your bot has the necessary permissions",
                    "4. Deploy your bot to Teams"
                },
                documentation = "https://docs.microsoft.com/en-us/microsoftteams/platform/bots/what-are-bots"
            },
            "outlook" => new
            {
                steps = new[]
                {
                    "1. Register the webhook in Microsoft Graph API",
                    "2. Configure the notification URL with this webhook URL",
                    "3. Set up the appropriate resource and change types",
                    "4. Handle the validation token during subscription creation"
                },
                documentation = "https://docs.microsoft.com/en-us/graph/webhooks"
            },
            _ => new
            {
                steps = new[]
                {
                    "1. Configure your application to send webhooks to the provided URL",
                    "2. Include any required authentication headers",
                    "3. Send POST requests with JSON payloads"
                },
                documentation = "See platform-specific documentation"
            }
        };
    }

    /// <summary>
    /// Returns the list of supported integration types with their metadata
    /// </summary>
    private static IResult GetIntegrationTypes()
    {
        var integrationTypes = new List<IntegrationTypeMetadata>
        {
            new IntegrationTypeMetadata
            {
                PlatformId = "slack",
                DisplayName = "Slack",
                Description = "Team collaboration and messaging platform",
                Icon = "slack",
                RequiredConfigurationFields = new List<ConfigurationFieldMetadata>
                {
                    new() { FieldName = "appId", DisplayName = "App ID", Description = "Slack App ID", IsSecret = false },
                    new() { FieldName = "teamId", DisplayName = "Team ID", Description = "Slack Workspace Team ID", IsSecret = false }
                },
                Capabilities = new List<string> { "bidirectional_messaging", "rich_formatting", "interactive_components", "file_sharing" },
                WebhookEndpoint = "/api/apps/slack/events/{integrationId}/{webhookSecret}",
                DocumentationUrl = "https://api.slack.com/docs"
            },
            new IntegrationTypeMetadata
            {
                PlatformId = "msteams",
                DisplayName = "Microsoft Teams",
                Description = "Enterprise team collaboration platform",
                Icon = "teams",
                RequiredConfigurationFields = new List<ConfigurationFieldMetadata>
                {
                    new() { FieldName = "appId", DisplayName = "App ID", Description = "Microsoft App ID", IsSecret = false },
                    new() { FieldName = "appTenantId", DisplayName = "App Tenant ID", Description = "Azure AD Tenant ID", IsSecret = false },
                    new() { FieldName = "serviceUrl", DisplayName = "Service URL", Description = "Bot Framework Service URL", IsSecret = false }
                },
                Capabilities = new List<string> { "bidirectional_messaging", "rich_formatting", "adaptive_cards", "file_sharing" },
                WebhookEndpoint = "/api/apps/msteams/messaging/{integrationId}/{webhookSecret}",
                DocumentationUrl = "https://docs.microsoft.com/en-us/microsoftteams/platform/"
            },
            // new IntegrationTypeMetadata
            // {
            //     PlatformId = "outlook",
            //     DisplayName = "Outlook",
            //     Description = "Email integration for Microsoft Outlook",
            //     Icon = "outlook",
            //     RequiredConfigurationFields = new List<ConfigurationFieldMetadata>
            //     {
            //         new() { FieldName = "userEmail", DisplayName = "User Email", Description = "Email address for the integration", IsSecret = false },
            //         new() { FieldName = "tenantId", DisplayName = "Tenant ID", Description = "Azure AD Tenant ID", IsSecret = false }
            //     },
            //     Capabilities = new List<string> { "email_messaging", "calendar_integration" },
            //     WebhookEndpoint = "/api/apps/outlook/events/{integrationId}/{webhookSecret}",
            //     DocumentationUrl = "https://docs.microsoft.com/en-us/graph/outlook-overview"
            // },
            // new IntegrationTypeMetadata
            // {
            //     PlatformId = "webhook",
            //     DisplayName = "Custom Webhook",
            //     Description = "Custom webhook integration for any platform",
            //     Icon = "webhook",
            //     RequiredConfigurationFields = new List<ConfigurationFieldMetadata>
            //     {
            //         new() { FieldName = "headers", DisplayName = "Custom Headers", Description = "Custom HTTP headers for outgoing requests", IsSecret = false }
            //     },
            //     Capabilities = new List<string> { "bidirectional_messaging", "custom_payloads" },
            //     WebhookEndpoint = "/api/apps/webhook/events/{integrationId}/{webhookSecret}",
            //     DocumentationUrl = null
            // }
        };

        return Results.Ok(integrationTypes);
    }
}
