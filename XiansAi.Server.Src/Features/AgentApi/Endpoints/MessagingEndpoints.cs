using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Features.WebApi.Services;

namespace Features.AgentApi.Endpoints
{
    public static class ConversationEndpoints
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversation")
                .WithTags("AgentAPI - Conversation")
                .RequiresCertificate();

            group.MapGet("/authorization/{authorizationGuid}", async (
                [FromRoute] string authorizationGuid,
                [FromServices] IAuthorizationCacheService authorizationCacheService) => {
                var authorization = await authorizationCacheService.GetAuthorization(authorizationGuid);
                if (authorization == null)
                {
                    return Results.NotFound($"No authorization found for GUID: {authorizationGuid}");
                }
                return Results.Ok(new { authorization });
            })
            .WithName("Get Authorization")
            .WithOpenApi(operation => {
                operation.Summary = "Get authorization by GUID";
                operation.Description = "Retrieves a cached authorization using its GUID";
                return operation;
            });

            group.MapGet("/history", async (
                [FromQuery] string agent,
                [FromQuery] string workflowType,
                [FromQuery] string participantId,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromServices] IMessageService messageService) => {
                
                var result = await messageService.GetThreadHistoryAsync(agent, workflowType, participantId, page, pageSize);
                return result.ToHttpResult();
            })
            .WithName("Get Conversation History")
            .WithOpenApi(operation => {
                operation.Summary = "Get conversation history";
                operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                return operation;
            });


            group.MapPost("/inbound", async (
            [FromBody] MessageRequest request, 
            [FromServices] IMessageService messageService) => {
                var result = await messageService.ProcessIncomingMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Inbound Message to Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process inbound message to Agent";
                operation.Description = "Processes an inbound message for agent conversations and returns the result";
                return operation;
            });


            group.MapPost("/outbound/send", async (
                [FromBody] MessageRequest request, 
                [FromServices] IMessageService messageService) => {
                var result = await messageService.ProcessOutgoingMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Message from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound message to Agent";
                operation.Description = "Processes an outbound message for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/handover", async (
                [FromBody] HandoverRequest request, 
                [FromServices] IMessageService messageService) => {
                var result = await messageService.ProcessHandover(request);
                return result.ToHttpResult();
            })
            .WithName("Process Handover Message from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process handover message from Agent";
                operation.Description = "Processes a handover message for agent conversations and returns the result";
                return operation;
            });

        }
    }
} 