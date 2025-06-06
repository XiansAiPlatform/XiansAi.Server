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
            var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            MapRoutes(group, version, registeredPaths);
            V1.WebhookEndpointsV1.MapRoutes(group, version, registeredPaths);
        }

        internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
        {
            string RouteKey(string method, string path) => $"{method}:{path}";

            // If v2 has the same endpoint, we can reuse it, before v1 is called this method will be called and hashset will record that it is already called
            // Hence v1 would not register the same endpoint again
            
            var regiserPath = "/register";
            if (registeredPaths.Add(RouteKey("POST", regiserPath)))
            {
                group.MapPost(regiserPath, async (
                    WebhookRegistrationDto registration,
                    IWebhookService webhookService,
                    HttpContext context) =>
                {
                    var webhook = await webhookService.RegisterWebhookAsync(registration);
                    return Results.Ok(webhook);
                })
                .WithName($"{version} - RegisterWebhook")
                .WithDescription("Register a new webhook for a workflow");
            }
        }
    }
} 