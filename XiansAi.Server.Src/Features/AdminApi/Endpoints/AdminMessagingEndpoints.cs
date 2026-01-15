using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Features.UserApi.Services;
using Features.WebApi.Utils;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Utils;

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
    private static void SetAuthorizationFromHeader(ChatOrDataRequest request, HttpContext context)
    {
        if (request.Authorization == null)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            var (success, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
            
            if (success && token != null)
            {
                request.Authorization = token;
            }
        }
    }

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
        .WithName("StreamThreadMessageEventsForAdminApi")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Stream Thread Message Events",
            Description = "Subscribe to real-time message events for a specific conversation thread using Server-Sent Events (SSE). Requires threadId parameter. Optional heartbeatSeconds parameter (1-300 seconds, default 5) for heartbeat interval. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Send Data to workflow endpoint
        adminMessagingGroup.MapPost("/inbound/data", async (
            string tenantId,
            [FromBody] ChatOrDataRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Data);
            return result.ToHttpResult();
        })
        .WithName("SendDataToWorkflowForAdminApi")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Send Data to Workflow",
            Description = "Send data to a workflow for a specific tenant. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });

        // Send Chat to workflow endpoint
        adminMessagingGroup.MapPost("/inbound/chat", async (
            string tenantId,
            [FromBody] ChatOrDataRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Chat);
            return result.ToHttpResult();
        })
        .WithName("SendChatToWorkflowForAdminApi")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Send Chat to Workflow",
            Description = "Send a chat message to a workflow for a specific tenant. Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header."
        });
    }
}

