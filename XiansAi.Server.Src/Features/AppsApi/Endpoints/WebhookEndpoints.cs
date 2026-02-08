using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Shared.Services;
using Shared.Repositories;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Generic webhook endpoints for custom webhook integrations.
/// These endpoints handle custom/generic webhook integrations that aren't platform-specific.
/// </summary>
public static class WebhookEndpoints
{
    /// <summary>
    /// Maps generic webhook endpoints.
    /// </summary>
    public static void MapWebhookEndpoints(this RouteGroupBuilder appsGroup)
    {
        // Generic webhook endpoint for custom integrations
        appsGroup.MapPost("/webhook/events/{integrationId}/{webhookSecret}", HandleWebhookEvents)
            .WithName("HandleWebhookEvents")
            .AllowAnonymous()
            .WithTags("AppsAPI - Webhook")
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Custom Webhook Events",
                Description = """
                    Dedicated endpoint for custom/generic webhook integrations.
                    
                    Handles webhook events from custom platforms or services that aren't 
                    directly supported (non-Slack, non-Teams, non-Outlook).
                    
                    **Security:**
                    - Webhook secret in URL provides first layer of defense
                    - Optional HMAC verification can be configured
                    
                    Configure this URL in your custom webhook provider's settings.
                    """
            });
    }

    /// <summary>
    /// Dedicated generic webhook handler
    /// </summary>
    private static async Task<IResult> HandleWebhookEvents(
        string integrationId,
        string webhookSecret,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] IMessageService messageService,
        [FromServices] ILogger<WebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received generic webhook for integration {IntegrationId}", integrationId);

            var integration = await integrationService.GetIntegrationEntityByIdAsync(integrationId);

            if (integration == null)
            {
                logger.LogWarning("Integration {IntegrationId} not found", integrationId);
                return Results.NotFound("Integration not found");
            }

            // Validate webhook secret FIRST (before any other processing)
            if (integration.Secrets?.WebhookSecret != webhookSecret)
            {
                logger.LogWarning("Invalid webhook secret for integration {IntegrationId}", integrationId);
                return Results.NotFound(); // Don't reveal if integration exists
            }

            if (!integration.IsEnabled)
            {
                logger.LogWarning("Integration {IntegrationId} is disabled", integrationId);
                return Results.StatusCode(503);
            }

            if (integration.PlatformId != "webhook")
            {
                logger.LogWarning("Integration {IntegrationId} is not a webhook integration (platform: {Platform})", 
                    integrationId, integration.PlatformId);
                return Results.BadRequest("This endpoint is only for webhook integrations");
            }

            // Read request body
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);
            httpContext.Request.Body.Position = 0;

            return await ProcessWebhookAsync(integration, rawBody, messageService, logger, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing generic webhook");
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }

    /// <summary>
    /// Processes generic webhook payloads
    /// </summary>
    private static async Task<IResult> ProcessWebhookAsync(
        AppIntegration integration,
        string rawBody,
        IMessageService messageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Generic webhook - no signature verification by default
        // Could optionally verify HMAC if secret is configured

        // Normalize participant ID to lowercase for consistency (especially important for emails)
        var participantId = (integration.MappingConfig.DefaultParticipantId ?? "webhook").ToLowerInvariant();
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
            logger.LogInformation("Successfully processed webhook for integration {IntegrationId}", integration.Id);
            return Results.Ok(new { status = "accepted", threadId = result.Data });
        }
        else
        {
            logger.LogError("Failed to process webhook for integration {IntegrationId}: {Error}", 
                integration.Id, result.ErrorMessage);
            return Results.Problem(result.ErrorMessage, statusCode: 500);
        }
    }
}

/// <summary>
/// Logger class for webhook endpoints
/// </summary>
public class WebhookEndpointsLogger { }
