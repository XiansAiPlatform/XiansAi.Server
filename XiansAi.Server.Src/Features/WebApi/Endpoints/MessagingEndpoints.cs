using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Features.WebApi.Services;
using Shared.Services;
using Shared.Repositories;
using Shared.Utils;

namespace Features.WebApi.Endpoints;

public static class MessagingEndpoints
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

    public static void MapMessagingEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var messagingGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging")
            .RequiresValidTenant()
            .RequireAuthorization();

        messagingGroup.MapPost("/inbound/data", async (
            [FromBody] ChatOrDataRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) => {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Data);
            return result.ToHttpResult();
        })
        .WithName("Send Data to workflow")
        
        .WithSummary("Send Data to workflow")
        .WithDescription("Send a data to a workflow");

        messagingGroup.MapPost("/inbound/chat", async (
            [FromBody] ChatOrDataRequest request, 
            [FromServices] IMessageService messageService,
            HttpContext context) => {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Chat);
            return result.ToHttpResult();
        })
        .WithName("Send Chat to workflow")
        
        .WithSummary("Send Chat to workflow")
        .WithDescription("Send a chat to a workflow");

        messagingGroup.MapGet("/threads", async (
            [FromServices] IMessagingService endpoint,
            [FromQuery] string agent,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) => {
            var result = await endpoint.GetThreads(agent, page, pageSize);  
            return result.ToHttpResult();
        })
        .WithName("Get AllThreads")
        
        .WithSummary("Get all threads for a workflow")
        .WithDescription("Gets all threads for a given workflow of a tenant");   

        messagingGroup.MapGet("/threads/{threadId}/messages", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null,
            [FromQuery] string? scope = null) => {
            var result = await endpoint.GetMessages(threadId, page, pageSize, scope);  
            return result.ToHttpResult();
        })
        .WithName("Get Messages for a thread")
        
        .WithSummary("Get all messages for a thread")
        .WithDescription("Gets all messages for a given thread. Optionally filter by scope (topic). Pass empty string or null to get messages with no scope.");

        messagingGroup.MapGet("/threads/{threadId}/topics", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50) => {
            var result = await endpoint.GetTopics(threadId, page, pageSize);  
            return result.ToHttpResult();
        })
        .WithName("Get Topics for a thread")
        
        .WithSummary("Get all topics (scopes) for a thread")
        .WithDescription("Gets all unique topics (scopes) for a given thread with message counts and last activity timestamp. Results are sorted by most recent activity first. Supports pagination with default page size of 50 and maximum of 100.");   

        messagingGroup.MapDelete("/threads/{threadId}", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId) => {
            var result = await endpoint.DeleteThread(threadId);
            return result.ToHttpResult();
        })
        .WithName("Delete Thread")
        
        .WithSummary("Delete a conversation thread")
        .WithDescription("Deletes a conversation thread and all its messages");
    }
} 