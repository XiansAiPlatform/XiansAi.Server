using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Features.UserApi.Services;
using Features.WebApi.Utils;
using Shared.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Logger class for AdminApi messaging endpoints.
/// </summary>
public class AdminMessagingEndpointsLogger { }

/// <summary>
/// AdminApi endpoints for messaging and communication.
/// These are administrative operations for managing real-time messaging via Server-Sent Events.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminMessagingEndpoints
{
    /// <summary>
    /// Maps all AdminApi messaging endpoints.
    /// </summary>
    public static void MapAdminMessagingEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminMessagingGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/messaging")
            .WithTags("AdminAPI - Messaging")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Server-Sent Events endpoint for real-time message streaming by thread
        adminMessagingGroup.MapGet("/threads/{threadId}/events", async (
            string tenantId,
            [FromRoute] string threadId,
            [FromServices] IMessageEventPublisher messageEventPublisher,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken,
            [FromQuery] int heartbeatSeconds = 5) =>
        {
            // Validate required parameters
            if (string.IsNullOrEmpty(threadId))
            {
                return Results.BadRequest("Thread ID is required");
            }

            // Create logger from factory
            var logger = loggerFactory.CreateLogger<AdminMessagingEndpointsLogger>();

            // Use the SSE stream handler to manage the entire connection lifecycle
            var streamHandler = new WebApiSSEStreamHandler(
                messageEventPublisher, 
                logger, 
                context, 
                threadId, 
                tenantContext, 
                cancellationToken,
                TimeSpan.FromSeconds(Math.Max(1, Math.Min(heartbeatSeconds, 300)))); // Between 1-300 seconds

            return await streamHandler.HandleStreamAsync();
        })
        .WithName("StreamThreadMessageEvents")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Stream Thread Message Events",
            Description = "Subscribe to real-time message events for a specific conversation thread using Server-Sent Events (SSE). Requires threadId parameter. Optional heartbeatSeconds parameter (1-300 seconds, default 5) for heartbeat interval. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });
    }
}

