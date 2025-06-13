using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;

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
                HttpContext context) =>
            {
                // Extract certificate from the Authorization header (Base64 encoded)
                var certHeader = context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(certHeader))
                {
                    // Store the certificate in the request
                    request.Token = certHeader;
                    request.AuthProvider = "Certificate";
                }

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