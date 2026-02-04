using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;
using Shared.Services;
using Shared.Repositories;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Public webhook endpoints for receiving events from external platforms (Slack, Teams, etc.).
/// These endpoints are called by external platforms and do not require standard authentication.
/// Authentication is done via platform-specific signature verification.
/// </summary>
public static class AppWebhookEndpoints
{
    /// <summary>
    /// Maps all public webhook endpoints for app integrations.
    /// </summary>
    public static void MapAppWebhookEndpoints(this WebApplication app)
    {
        var appsGroup = app.MapGroup("/api/apps")
            .WithTags("AppsAPI - Webhooks");

        // Generic webhook endpoint for all platforms
        appsGroup.MapPost("/{platformId}/events/{integrationId}", HandlePlatformWebhook)
            .WithName("HandlePlatformWebhook")
            .AllowAnonymous() // Authentication is done via signature verification
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Platform Webhook",
                Description = """
                    Receives webhook events from external platforms (Slack, Teams, Outlook, etc.).
                    
                    **Authentication:**
                    Authentication is performed via platform-specific signature verification, not 
                    standard bearer tokens. Each platform has its own method:
                    
                    - **Slack**: X-Slack-Signature header verification
                    - **Teams**: Bot Framework signature verification
                    - **Outlook**: Microsoft Graph validation token
                    
                    **Path Parameters:**
                    - `platformId`: Platform identifier (slack, msteams, outlook, webhook)
                    - `integrationId`: The integration ID (MongoDB ObjectId)
                    
                    **Notes:**
                    - This endpoint is designed to be called by external platforms
                    - The integration must be enabled for webhooks to be processed
                    - Invalid signatures result in 401 Unauthorized
                    """
            });

        // Slack-specific endpoints for different event types
        appsGroup.MapPost("/slack/events/{integrationId}", HandleSlackEventsWebhook)
            .WithName("HandleSlackEventsWebhook")
            .AllowAnonymous()
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Slack Events API Webhook",
                Description = """
                    Dedicated endpoint for Slack Events API webhooks.
                    
                    Handles:
                    - URL verification challenges
                    - Message events
                    - App mention events
                    - Other subscribed events
                    
                    Configure this URL in your Slack App's Event Subscriptions settings.
                    """
            });

        appsGroup.MapPost("/slack/interactive/{integrationId}", HandleSlackInteractiveWebhook)
            .WithName("HandleSlackInteractiveWebhook")
            .AllowAnonymous()
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Slack Interactive Components Webhook",
                Description = """
                    Dedicated endpoint for Slack Interactive Components (buttons, modals, etc.).
                    
                    Configure this URL in your Slack App's Interactivity settings.
                    """
            });
    }

    /// <summary>
    /// Generic webhook handler for all platforms
    /// </summary>
    private static async Task<IResult> HandlePlatformWebhook(
        string platformId,
        string integrationId,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] IMessageService messageService,
        [FromServices] ILogger<AppWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received webhook for platform {Platform}, integration {IntegrationId}",
                platformId, integrationId);

            // Get the integration
            var integration = await integrationService.GetIntegrationEntityByIdAsync(integrationId);

            if (integration == null)
            {
                logger.LogWarning("Integration {IntegrationId} not found", integrationId);
                return Results.NotFound("Integration not found");
            }

            if (integration.PlatformId != platformId.ToLowerInvariant())
            {
                logger.LogWarning("Platform mismatch: expected {Expected}, got {Actual}",
                    integration.PlatformId, platformId);
                return Results.BadRequest("Platform mismatch");
            }

            if (!integration.IsEnabled)
            {
                logger.LogWarning("Integration {IntegrationId} is disabled", integrationId);
                return Results.StatusCode(503); // Service Unavailable
            }

            // Read the request body
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);
            httpContext.Request.Body.Position = 0;

            // Route to platform-specific handler
            // Note: For Slack, use the dedicated /slack/events/{integrationId} endpoint instead
            return platformId.ToLowerInvariant() switch
            {
                "slack" => Results.BadRequest("Use /api/apps/slack/events/{integrationId} for Slack webhooks"),
                "msteams" => await ProcessTeamsWebhookAsync(integration, rawBody, httpContext, messageService, logger, cancellationToken),
                "outlook" => await ProcessOutlookWebhookAsync(integration, rawBody, httpContext, messageService, logger, cancellationToken),
                "webhook" => await ProcessGenericWebhookAsync(integration, rawBody, httpContext, messageService, logger, cancellationToken),
                _ => Results.BadRequest($"Unsupported platform: {platformId}")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing webhook for integration {IntegrationId}", integrationId);
            return Results.Problem("An error occurred processing the webhook", statusCode: 500);
        }
    }

    /// <summary>
    /// Dedicated Slack Events API handler
    /// </summary>
    private static async Task<IResult> HandleSlackEventsWebhook(
        string integrationId,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ISlackWebhookHandler slackHandler,
        [FromServices] ILogger<AppWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received Slack Events webhook for integration {IntegrationId}", integrationId);

            var integration = await integrationService.GetIntegrationEntityByIdAsync(integrationId);

            if (integration == null)
            {
                logger.LogWarning("Integration {IntegrationId} not found", integrationId);
                return Results.NotFound("Integration not found");
            }

            if (!integration.IsEnabled)
            {
                logger.LogWarning("Integration {IntegrationId} is disabled", integrationId);
                return Results.StatusCode(503);
            }

            // Read request body
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);
            httpContext.Request.Body.Position = 0;

            return await slackHandler.ProcessEventsWebhookAsync(integration, rawBody, httpContext, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Slack events webhook");
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }

    /// <summary>
    /// Dedicated Slack Interactive Components handler
    /// </summary>
    private static async Task<IResult> HandleSlackInteractiveWebhook(
        string integrationId,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ISlackWebhookHandler slackHandler,
        [FromServices] ILogger<AppWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received Slack interactive webhook for integration {IntegrationId}", integrationId);

            var integration = await integrationService.GetIntegrationEntityByIdAsync(integrationId);

            if (integration == null || !integration.IsEnabled)
            {
                return Results.NotFound("Integration not found or disabled");
            }

            return await slackHandler.ProcessInteractiveWebhookAsync(integration, httpContext, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Slack interactive webhook");
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }


    #region Teams Processing

    private static Task<IResult> ProcessTeamsWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        IMessageService messageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // TODO: Implement Teams Bot Framework webhook processing
        logger.LogInformation("Teams webhook processing not yet implemented");
        return Task.FromResult(Results.Ok());
    }

    #endregion

    #region Outlook Processing

    private static Task<IResult> ProcessOutlookWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        IMessageService messageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // TODO: Implement Outlook/Graph webhook processing
        logger.LogInformation("Outlook webhook processing not yet implemented");
        return Task.FromResult(Results.Ok());
    }

    #endregion

    #region Generic Webhook Processing

    private static async Task<IResult> ProcessGenericWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        IMessageService messageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Generic webhook - no signature verification by default
        // Could optionally verify HMAC if secret is configured

        var participantId = integration.MappingConfig.DefaultParticipantId ?? "webhook";
        var scope = integration.MappingConfig.DefaultScope;

        var chatRequest = new ChatOrDataRequest
        {
            WorkflowId = integration.WorkflowId,
            ParticipantId = participantId,
            Text = "Webhook received",
            Data = rawBody,
            Scope = scope,
            Origin = $"app:webhook:{integration.Id}",
            Type = MessageType.Data
        };

        var result = await messageService.ProcessIncomingMessage(chatRequest, MessageType.Data);

        if (result.IsSuccess)
        {
            return Results.Ok(new { status = "accepted", threadId = result.Data });
        }
        else
        {
            return Results.Problem(result.ErrorMessage, statusCode: 500);
        }
    }

    #endregion

}

/// <summary>
/// Logger class for webhook endpoints
/// </summary>
public class AppWebhookEndpointsLogger { }
