using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;

namespace Features.AgentApi.Endpoints.V1
{
    public static class ConversationEndpointsV1
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var version = "v1";
            var group = app.MapGroup($"/api/{version}/agent/conversation")
                .WithTags($"AgentAPI - Conversation {version}")
                .RequiresCertificate();

            var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MapRoutes(group, version, registeredPaths);
        }

        internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
        {
        string RouteKey(string method, string path) => $"{method}:{path}";
        
            if (registeredPaths.Add(RouteKey("GET", "/history")))
            {
                group.MapGet("/history", async (
                [FromQuery] string workflowType,
                [FromQuery] string participantId,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromServices] IMessageService messageService) =>
                {

                    var result = await messageService.GetThreadHistoryAsync(workflowType, participantId, page, pageSize);
                    return result.ToHttpResult();
                })
                .WithName($"{version} - Get Conversation History")
                .WithOpenApi(operation =>
                {
                    operation.Summary = "Get conversation history";
                    operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                    return operation;
                });
            }

            if (registeredPaths.Add(RouteKey("POST", "/outbound/chat")))
            {
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
            }

            if (registeredPaths.Add(RouteKey("POST", "/outbound/data")))
            {
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
            }

            if (registeredPaths.Add(RouteKey("POST", "/outbound/handoff")))
            {
                group.MapPost("/outbound/handoff", async (
                [FromBody] HandoffRequest request, 
                [FromServices] IMessageService messageService) => {
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
} 