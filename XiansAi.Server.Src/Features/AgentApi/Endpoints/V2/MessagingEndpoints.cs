using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Microsoft.AspNetCore.SignalR;
using XiansAi.Server.Shared.Websocket;

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
            
            // Reuse v1 mappings
            V1.ConversationEndpointsV1.CommonMapRoutes(group, version);

            // If there are any routes that will be deleted in future versions, add them here
            UniqueMapRoutes(group, version);
        }

        internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
        {
            // You can add new routes specific to v2 here if needed
            // For now, we are reusing the v1 mappings
        }
    }
} 