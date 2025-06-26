using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;

namespace Features.AgentApi.Endpoints
{
    public static class ConversationEndpoints
    {
        private static void SetAuthorizationFromHeader(HandoffRequest request, HttpContext context)
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

        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversation")
                .WithTags("AgentAPI - Conversation")
                .RequiresCertificate();

            group.MapGet("/history", async (
                [FromQuery] string workflowType,
                [FromQuery] string participantId,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromServices] IMessageService messageService) => {
                
                var result = await messageService.GetThreadHistoryAsync(workflowType, participantId, page, pageSize);
                return result.ToHttpResult();
            })
            .WithName("Get Conversation History")
            .WithOpenApi(operation => {
                operation.Summary = "Get conversation history";
                operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                return operation;
            });

            //group.MapGet("authorization/{authorizationGuid}", async (
            //    [FromRoute] string authorizationGuid,
            //    [FromServices] IMessageService messageService) => {
            //    var result = await messageService.GetAuthorization(authorizationGuid);
            //    return result.ToHttpResult();
            //})
            //.WithName("Get Authorization")
            //.WithOpenApi(operation => {
            //    operation.Summary = "Get authorization by GUID";
            //    operation.Description = "Retrieves a cached authorization using its GUID";
            //    return operation;
            //});

            group.MapPost("/outbound/chat", async (
                [FromBody] ChatOrDataRequest request, 
                [FromServices] IMessageService messageService) => {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Chat);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Chat from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound chat from Agent";
                operation.Description = "Processes an outbound chat for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/data", async (
                [FromBody] ChatOrDataRequest request, 
                [FromServices] IMessageService messageService) => {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Data);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Data from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound data from Agent";
                operation.Description = "Processes an outbound data for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/handoff", async (
                [FromBody] HandoffRequest request, 
                [FromServices] IMessageService messageService,
                HttpContext context) => {
                SetAuthorizationFromHeader(request, context);
                var result = await messageService.ProcessHandoff(request);
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