using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Features.WebApi.Services;
using Shared.Services;

namespace Features.WebApi.Endpoints;

public static class MessagingEndpoints
{
    private static async Task<string> GetAuthorizationGuid(string? requestAuth, HttpContext httpContext, IAuthorizationCacheService authorizationCacheService)
    {
        if (requestAuth == null)
        {
            requestAuth = httpContext.Request.Headers["Authorization"].ToString().Split(" ")[1];
        }
        return await authorizationCacheService.CacheAuthorization(requestAuth);
    }

    public static void MapMessagingEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var messagingGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging")
            .RequiresValidTenant();

        messagingGroup.MapPost("/inbound", async (
            [FromBody] MessageRequest request, 
            [FromServices] IMessageService messageService,
            [FromServices] IAuthorizationCacheService tokenCacheService,
            HttpContext httpContext) => {
            request.Authorization = await GetAuthorizationGuid(request.Authorization, httpContext, tokenCacheService);
            var result = await messageService.ProcessIncomingMessage(request);
            return result.ToHttpResult();
        })
        .WithName("Send Message to workflow")
        .WithOpenApi(operation => {
            operation.Summary = "Send Message to workflow";
            operation.Description = "Send a message to a workflow";
            return operation;
        });

        messagingGroup.MapGet("/threads", async (
            [FromServices] IMessagingService endpoint,
            [FromQuery] string agent,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) => {
            var result = await endpoint.GetThreads(agent, page, pageSize);  
            return result.ToHttpResult();
        })
        .WithName("Get AllThreads")
        .WithOpenApi(operation => {
            operation.Summary = "Get all threads for a workflow";
            operation.Description = "Gets all threads for a given workflow of a tenant";
            return operation;
        });   

        messagingGroup.MapGet("/threads/{threadId}/messages", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId,
            [FromQuery] int? page = null,
            [FromQuery] int? pageSize = null) => {
            var result = await endpoint.GetMessages(threadId, page, pageSize);  
            return result.ToHttpResult();
        })
        .WithName("Get Messages for a thread")
        .WithOpenApi(operation => {
            operation.Summary = "Get all messages for a thread";
            operation.Description = "Gets all messages for a given thread";
            return operation;
        });   

        messagingGroup.MapDelete("/threads/{threadId}", async (
            [FromServices] IMessagingService endpoint,
            [FromRoute] string threadId) => {
            var result = await endpoint.DeleteThread(threadId);
            return result.ToHttpResult();
        })
        .WithName("Delete Thread")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a conversation thread";
            operation.Description = "Deletes a conversation thread and all its messages";
            return operation;
        });
    }
} 