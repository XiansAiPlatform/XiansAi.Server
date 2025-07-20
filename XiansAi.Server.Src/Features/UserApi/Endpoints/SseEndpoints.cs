using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Features.UserApi.Services;
using Features.UserApi.Utils;

namespace Features.UserApi.Endpoints;

public class SseEndpointsLogger { }

public static class SseEndpoints
{
    public static void MapSseEndpoints(this WebApplication app)
    {
        var sseGroup = app.MapGroup("/api/user/sse")
            .WithTags("UserAPI - Server-Sent Events")
            .RequireAuthorization("EndpointAuthPolicy");

        // Server-Sent Events endpoint for real-time message streaming
        sseGroup.MapGet("/events", async (
            [FromQuery] string workflow,
            [FromQuery] string participantId,
            [FromServices] IMessageEventPublisher messageEventPublisher,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken,
            [FromQuery] string? scope = null,
            [FromQuery] int heartbeatSeconds = 5) =>
        {
            // Validate required parameters
            if (string.IsNullOrEmpty(workflow) || string.IsNullOrEmpty(participantId))
            {
                return Results.BadRequest("Workflow and participantId are required parameters");
            }

            // Create logger from factory
            var logger = loggerFactory.CreateLogger<SseEndpointsLogger>();

            // Use the SSE stream handler to manage the entire connection lifecycle
            var streamHandler = new SSEStreamHandler(
                messageEventPublisher, 
                logger, 
                context, 
                workflow, 
                participantId, 
                tenantContext, 
                scope,
                cancellationToken,
                TimeSpan.FromSeconds(Math.Max(1, Math.Min(heartbeatSeconds, 300)))); // Between 1-300 seconds

            return await streamHandler.HandleStreamAsync();
        })
        .WithName("StreamMessageEvents")
        .WithSummary("Stream real-time messages via Server-Sent Events")
        .WithDescription("Subscribe to real-time message events for a specific workflow and participant using Server-Sent Events (SSE). Requires workflow and participantId parameters. Optional scope parameter for filtering messages. Optional heartbeatSeconds parameter (1-300 seconds, default 5) for heartbeat interval.");
    }
} 