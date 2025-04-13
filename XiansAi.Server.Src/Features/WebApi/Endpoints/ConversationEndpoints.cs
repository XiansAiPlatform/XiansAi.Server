using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class ConversationEndpoints
{
    public static void MapConversationEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/client/conversations")
            .WithTags("WebAPI - Conversations")
            .RequiresValidTenant();

        group.MapPost("/inbound", async (
            [FromBody] InboundMessageRequest request, 
            [FromServices] IConversationService conversationService) => {
            var result = await conversationService.ProcessInboundMessage(request);
            return result.ToHttpResult();
        })
        .WithName("WebAPI - Inbound Message")
        .WithOpenApi(operation => {
            operation.Summary = "Process inbound conversation message";
            operation.Description = "Processes an inbound message for agent conversations and returns the result";
            return operation;
        });

    }
}
