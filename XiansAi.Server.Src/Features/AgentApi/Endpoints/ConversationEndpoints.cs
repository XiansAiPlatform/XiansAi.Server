using Features.AgentApi.Services.Agent;
using Features.AgentApi.Auth;
using Shared.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Features.AgentApi.Endpoints
{
    public static class ConversationEndpoints
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversations")
                .WithTags("AgentAPI - Conversations")
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

        }
    }
} 