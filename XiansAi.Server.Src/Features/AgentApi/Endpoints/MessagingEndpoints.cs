using Shared.Services;
using Shared.Utils;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Auth;
using Microsoft.AspNetCore.SignalR;
using XiansAi.Server.Shared.Websocket;

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
                [FromQuery] string agent,
                [FromQuery] string participantId,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromServices] IMessageService messageService) => {
                
                var result = await messageService.GetThreadHistoryAsync(agent, participantId, page, pageSize);
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
                [FromServices] IMessageService messageService,
                [FromServices] IHubContext<ChatHub> hubContext,
                [FromServices] ClientConnectionManager connectionManager) => {
                var result = await messageService.ProcessOutgoingMessage(request);
                var connectionId = connectionManager.GetConnectionId(result.Data);
                if (connectionId != null)
                {
                    await hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", request.Content);
                    Console.WriteLine($"Sent message to thread {result.Data}");
                }
                else
                {
                    Console.WriteLine($"No client connection for thread {request.ThreadId}");
                }

                Console.WriteLine($"-----------/outbound/send--{result.Data}");
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