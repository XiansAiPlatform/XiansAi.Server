using XiansAi.Server.Features.AgentApi.Models;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using Features.AgentApi.Auth;

namespace Features.AgentApi.Endpoints.V2
{
    public static class WebhookEndpointsV2
    {
        public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var version = "v2";
            var group = endpoints.MapGroup($"/api/{version}/agent/webhooks")
                .WithTags($"AgentAPI - Webhooks {version}")
                .RequiresCertificate();

            // Reuse v1 mappings
            V1.WebhookEndpointsV1.CommonMapRoutes(group, version);

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