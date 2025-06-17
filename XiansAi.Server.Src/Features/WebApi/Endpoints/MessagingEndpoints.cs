using Features.WebApi.Auth;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class MessagingEndpoints
{
    private static void SetAuthorizationFromHeader(ChatOrDataRequest request, HttpContext context)
    {
        if (request.Authorization == null)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                request.Authorization = authHeader.Substring("Bearer ".Length).Trim();
            }
        }
    }

    public static void MapMessagingEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var messagingGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging")
            .RequiresValidTenant();

        messagingGroup.MapPost("/inbound/data", async (
            [FromBody] ChatOrDataRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Data);
            return result.ToHttpResult();
        })
        .WithName("Send Data to workflow")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Send Data to workflow";
            operation.Description = "Send a data to a workflow";
            return operation;
        });

        messagingGroup.MapPost("/inbound/chat", async (
            [FromBody] ChatOrDataRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            SetAuthorizationFromHeader(request, context);
            var result = await messageService.ProcessIncomingMessage(request, MessageType.Chat);
            return result.ToHttpResult();
        })
        .WithName("Send Chat to workflow")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Send Chat to workflow";
            operation.Description = "Send a chat to a workflow";
            return operation;
        });

        messagingGroup.MapGet("/threads", async (
            [FromServices] IMessagingService endpoint,
            [FromQuery] string agent,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) =>
        {
            var result = await endpoint.GetThreads(agent, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("Get AllThreads")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all threads for a workflow";
            operation.Description = "Gets all threads for a given workflow of a tenant";
            return operation;
        });

        messagingGroup.MapGet("/threads/{threadId}/messages", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) =>
        {
            var result = await endpoint.GetMessages(threadId, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("Get Messages for a thread")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get all messages for a thread";
            operation.Description = "Gets all messages for a given thread";
            return operation;
        });

        messagingGroup.MapDelete("/threads/{threadId}", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId) =>
        {
            var result = await endpoint.DeleteThread(threadId);
            return result.ToHttpResult();
        })
        .WithName("Delete Thread")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Delete a conversation thread";
            operation.Description = "Deletes a conversation thread and all its messages";
            return operation;
        });
    }
}