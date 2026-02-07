using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Slack-specific webhook endpoints for receiving events from Slack platform.
/// These endpoints are called by Slack and do not require standard authentication.
/// Authentication is done via Slack signature verification.
/// </summary>
public static class SlackWebhookEndpoints
{
    /// <summary>
    /// Maps Slack webhook endpoints.
    /// </summary>
    public static void MapSlackWebhookEndpoints(this RouteGroupBuilder appsGroup)
    {
        // Slack Events API endpoint
        appsGroup.MapPost("/slack/events/{integrationId}/{webhookSecret}", HandleSlackEventsWebhook)
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
                    
                    **Security:**
                    - Webhook secret in URL provides first layer of defense
                    - Slack signature verification provides second layer
                    
                    Configure this URL in your Slack App's Event Subscriptions settings.
                    """
            });

        // Slack Interactive Components endpoint
        appsGroup.MapPost("/slack/interactive/{integrationId}/{webhookSecret}", HandleSlackInteractiveWebhook)
            .WithName("HandleSlackInteractiveWebhook")
            .AllowAnonymous()
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Slack Interactive Components Webhook",
                Description = """
                    Dedicated endpoint for Slack Interactive Components (buttons, modals, etc.).
                    
                    **Security:**
                    - Webhook secret in URL provides first layer of defense
                    - Slack signature verification provides second layer
                    
                    Configure this URL in your Slack App's Interactivity settings.
                    """
            });
    }

    /// <summary>
    /// Dedicated Slack Events API handler
    /// </summary>
    private static async Task<IResult> HandleSlackEventsWebhook(
        string integrationId,
        string webhookSecret,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ISlackWebhookHandler slackHandler,
        [FromServices] ILogger<SlackWebhookEndpointsLogger> logger,
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
        string webhookSecret,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ISlackWebhookHandler slackHandler,
        [FromServices] ILogger<SlackWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received Slack interactive webhook for integration {IntegrationId}", integrationId);

            var integration = await integrationService.GetIntegrationEntityByIdAsync(integrationId);

            if (integration == null)
            {
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
}

/// <summary>
/// Logger class for Slack webhook endpoints
/// </summary>
public class SlackWebhookEndpointsLogger { }
