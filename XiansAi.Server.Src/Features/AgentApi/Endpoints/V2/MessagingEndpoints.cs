using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Microsoft.AspNetCore.SignalR;
using XiansAi.Server.Shared.Websocket;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2
{
    public static class ConversationEndpointsV2
    {
        public static void MapConversationEndpoints(WebApplication app)
        {
            var version = "v2";
            var group = app.MapGroup($"/api/{version}/agent/conversation")
                .WithTags($"AgentAPI - Conversation {version}")
                .RequiresCertificate();

            var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MapRoutes(group, version, registeredPaths);

            // Reuse v1 mappings
            V1.ConversationEndpointsV1.MapRoutes(group, version, registeredPaths);
        }

        internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
        {
            // string RouteKey(string method, string path) => $"{method}:{path}";

            // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
            // Hence v1 would not register the same endpoint again

            // var historyPath = "/history";
            // if (registeredPaths.Add(RouteKey("GET", historyPath)))
            // {
            //     group.MapGet(historyPath, async (
            //         [FromQuery] string agent,
            //         [FromQuery] string workflowType,
            //         [FromQuery] string participantId,
            //         [FromQuery] int page,
            //         [FromQuery] int pageSize,
            //         [FromServices] IMessageService messageService) =>
            //     {

            //         var result = await messageService.GetThreadHistoryAsync(agent, workflowType, participantId, page, pageSize);
            //         return result.ToHttpResult();
            //     })
            //     .WithName($"{version} - Get Conversation History")
            //     .WithOpenApi(operation =>
            //     {
            //         operation.Summary = "Get conversation history";
            //         operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
            //         return operation;
            //     });
            // }
        }
    }
} 