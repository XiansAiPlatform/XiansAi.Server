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
        appsGroup.MapPost("/msteams/messaging/{integrationId}", HandleTeamsMessagingWebhook)
            .WithName("HandleTeamsMessagingWebhook")
            .AllowAnonymous()
            .WithOpenApi(operation => new(operation)
            {
                Summary = "Handle Microsoft Teams Bot Framework Webhook",
                Description = """
                    Dedicated endpoint for Microsoft Teams Bot Framework activities.
                    
                    Handles:
                    - Message activities
                    - Conversation updates
                    - Invoke activities (adaptive card actions)
                    
                    Configure this URL as the messaging endpoint in Azure Bot Service.
                    """
            });
    }

    /// <summary>
    /// Dedicated Microsoft Teams Bot Framework handler
    /// </summary>
    private static async Task<IResult> HandleTeamsMessagingWebhook(
        string integrationId,
        HttpContext httpContext,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ITeamsWebhookHandler teamsHandler,
        [FromServices] ILogger<TeamsWebhookEndpointsLogger> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Received Teams messaging webhook for integration {IntegrationId}", integrationId);

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

            return await teamsHandler.ProcessActivityWebhookAsync(integration, rawBody, httpContext, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing Teams messaging webhook");
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }
}

/// <summary>
/// Logger class for Teams webhook endpoints
/// </summary>
public class TeamsWebhookEndpointsLogger { }
