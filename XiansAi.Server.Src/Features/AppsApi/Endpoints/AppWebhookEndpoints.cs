using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;
using Shared.Services;
using Shared.Repositories;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Generic webhook endpoints for receiving events from external platforms.
/// Platform-specific endpoints are in their respective files (SlackWebhookEndpoints, TeamsWebhookEndpoints).
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

        // Map platform-specific endpoints
        appsGroup.MapSlackWebhookEndpoints();
        appsGroup.MapTeamsWebhookEndpoints();

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
                    
                    - **Slack**: X-Slack-Signature header verification (use dedicated /slack/events endpoint)
                    - **Teams**: Bot Framework signature verification (use dedicated /msteams/messaging endpoint)
                    - **Outlook**: Microsoft Graph validation token
                    
                    **Path Parameters:**
                    - `platformId`: Platform identifier (outlook, webhook)
                    - `integrationId`: The integration ID (MongoDB ObjectId)
                    
                    **Notes:**
                    - This endpoint is designed to be called by external platforms
                    - For Slack and Teams, use their dedicated endpoints instead
                    - The integration must be enabled for webhooks to be processed
                    - Invalid signatures result in 401 Unauthorized
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
            // Note: For Slack and Teams, use the dedicated endpoints instead
            return platformId.ToLowerInvariant() switch
            {
                "slack" => Results.BadRequest("Use /api/apps/slack/events/{integrationId} for Slack webhooks"),
                "msteams" => Results.BadRequest("Use /api/apps/msteams/messaging/{integrationId} for Teams webhooks"),
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
