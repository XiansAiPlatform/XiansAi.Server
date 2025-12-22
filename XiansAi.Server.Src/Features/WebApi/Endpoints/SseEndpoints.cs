using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Features.UserApi.Services;
using Features.WebApi.Utils;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public class WebApiSseEndpointsLogger { }

public static class WebApiSseEndpoints
{
    public static void MapWebApiSseEndpoints(this WebApplication app)
    {
        var sseGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging SSE")
            .RequiresValidTenant()
            .RequireAuthorization();

        // Server-Sent Events endpoint for real-time message streaming by thread
        sseGroup.MapGet("/threads/{threadId}/events", async (
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
            var logger = loggerFactory.CreateLogger<WebApiSseEndpointsLogger>();

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
        .WithSummary("Stream real-time messages for a thread via Server-Sent Events")
        .WithDescription("Subscribe to real-time message events for a specific conversation thread using Server-Sent Events (SSE). Requires threadId parameter. Optional heartbeatSeconds parameter (1-300 seconds, default 5) for heartbeat interval.");
    }
}

