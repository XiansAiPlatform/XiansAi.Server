using Shared.Services;
using Shared.Utils;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints
{
    public static class ConversationEndpoints
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/messaging")
                .WithTags("AgentAPI - Messaging")
                .RequiresCertificate();

            group.MapPost("/inbound", async (
                [FromBody] InboundMessageRequest request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessInboundMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Inbound Message")
            .WithOpenApi(operation => {
                operation.Summary = "Process inbound conversation message";
                operation.Description = "Processes an inbound message for agent conversations and returns the result";
                return operation;
            });


            group.MapPost("/outbound", async (
                [FromBody] OutboundMessageRequest request, 
                [FromServices] IConversationService conversationService) => {
                var result = await conversationService.ProcessOutboundMessage(request);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Message")
            .WithOpenApi(operation => {
                operation.Summary = "Process outbound conversation message";
                operation.Description = "Processes an outbound message for agent conversations and returns the result";
                return operation;
            });
        }
    }
} 