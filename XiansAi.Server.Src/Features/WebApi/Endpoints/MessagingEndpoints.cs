using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using Shared.Services;

namespace Features.WebApi.Endpoints;

public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var messagingGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging")
            .RequiresValidTenant();


        messagingGroup.MapPost("/inbound", async (
            [FromBody] InboundMessageRequest request, 
            [FromServices] IConversationService conversationService) => {
            var result = await conversationService.ProcessInboundMessage(request);
            return result.ToHttpResult();
        })
        .WithName("Send Message to workflow")
        .WithOpenApi(operation => {
            operation.Summary = "Send Message to workflow";
            operation.Description = "Send a message to a workflow";
            return operation;
        });
        
        messagingGroup.MapGet("/agents", async (
            [FromServices] IMessagingService endpoint) =>
        {
            return await endpoint.GetGroupedDefinitions();
        })
        .WithName("Get Grouped Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get grouped definitions";
            operation.Description = "Retrieves grouped definitions";
            return operation;
        });

        messagingGroup.MapGet("/workflows", async (
            [FromQuery] string? agentName,
            [FromQuery] string? typeName,
            [FromServices] IMessagingService endpoint) =>
        {
            return await endpoint.GetWorkflowInstances(agentName, typeName);
        })
        .WithName("Get Workflow Instances")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow instances";
            operation.Description = "Retrieves workflow instances";
            return operation;
        });     

        messagingGroup.MapGet("/threads", async (
            [FromServices] IMessagingService endpoint,
            [FromQuery] string workflowId) => {
            var result = await endpoint.GetThreads(workflowId);  
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
    }
} 