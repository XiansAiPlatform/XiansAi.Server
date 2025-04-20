using Shared.Services;
using Shared.Utils;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Auth;

namespace Features.AgentApi.Endpoints
{
    public static class ConversationEndpoints
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversation")
                .WithTags("AgentAPI - Conversation")
                .RequiresCertificate();

            group.MapGet("/history", async (
                [FromQuery] string workflowId,
                [FromQuery] string participantId,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromServices] IConversationService conversationService) => {
                
                var result = await conversationService.GetMessageHistoryAsync(workflowId, participantId, page, pageSize);
                return result.ToHttpResult();
            })
            .WithName("Get Conversation History")
            .WithOpenApi(operation => {
                operation.Summary = "Get conversation history";
                operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                return operation;
            });


            group.MapPost("/inbound", async (
                [FromBody] InboundSignalRequest request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessInboundMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Inbound Message to Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process inbound message to Agent";
                operation.Description = "Processes an inbound message for agent conversations and returns the result";
                return operation;
            });


            group.MapPost("/outbound/send", async (
                [FromBody] OutboundSendRequest request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessOutboundMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Message from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound message to Agent";
                operation.Description = "Processes an outbound message for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/handover", async (
                [FromBody] OutboundHandoverRequest request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessOutboundHandover(request);
                return result.ToHttpResult();
            })
            .WithName("Process Handover Message from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process handover message from Agent";
                operation.Description = "Processes a handover message for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/handover-response", async (
                [FromBody] OutboundHandoverResponse request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessOutboundHandoverResponse(request);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Hand Over Response from Agent")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound hand over response from Agent";
                operation.Description = "Processes an outbound hand over response for agent conversations and returns the result";
                return operation;
            });
        }
    }
} 