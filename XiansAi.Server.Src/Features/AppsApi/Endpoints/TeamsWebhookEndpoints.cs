using System.Text;
using Microsoft.AspNetCore.Mvc;
using Features.AppsApi.Models;
using Features.AppsApi.Services;
using Features.AppsApi.Handlers;

namespace Features.AppsApi.Endpoints;

/// <summary>
/// Microsoft Teams-specific webhook endpoints for receiving Bot Framework activities.
/// These endpoints are called by Teams/Bot Framework and do not require standard authentication.
/// Authentication is done via Bot Framework signature verification.
/// </summary>
public static class TeamsWebhookEndpoints
{
    /// <summary>
    /// Maps Microsoft Teams webhook endpoints.
    /// </summary>
    public static void MapTeamsWebhookEndpoints(this RouteGroupBuilder appsGroup)
    {
        // Microsoft Teams Bot Framework endpoint
        appsGroup.MapPost("/msteams/events/{integrationId}/{webhookSecret}", HandleTeamsEventsWebhook)
            .WithName("HandleTeamsEventsWebhook")
            .AllowAnonymous()
            .WithTags("AppsAPI - Teams")
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Microsoft Teams Bot Framework Webhook",
                Description = """
                    Dedicated endpoint for Microsoft Teams Bot Framework activities.
                    
                    Handles:
                    - Message activities
                    - Conversation updates
                    - Invoke activities (adaptive card actions)
                    
                    **Security:**
                    - Webhook secret in URL provides first layer of defense
                    - Bot Framework JWT verification provides second layer
                    
                    Configure this URL as the messaging endpoint in Azure Bot Service.
                    """
            });
    }

    /// <summary>
    /// Dedicated Microsoft Teams Bot Framework handler
    /// </summary>
    private static async Task<IResult> HandleTeamsEventsWebhook(
        string integrationId,
        string webhookSecret,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ITeamsWebhookHandler teamsHandler,
        [FromServices] ILogger<TeamsWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received Teams events webhook for integration {IntegrationId}", integrationId);

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

            return await teamsHandler.ProcessActivityWebhookAsync(integration, rawBody, httpContext, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Teams events webhook");
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }
}

/// <summary>
/// Logger class for Teams webhook endpoints
/// </summary>
public class TeamsWebhookEndpointsLogger { }
