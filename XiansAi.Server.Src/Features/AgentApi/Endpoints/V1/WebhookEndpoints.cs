using XiansAi.Server.Features.AgentApi.Models;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using Features.AgentApi.Auth;

namespace Features.AgentApi.Endpoints.V1
{
    public static class WebhookEndpointsV1
    {
        public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var version = "v1";
            var group = endpoints.MapGroup($"/api/{version}/agent/webhooks")
                .WithTags($"AgentAPI - Webhooks {version}")
                .RequiresCertificate();

            var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MapRoutes(group, version, registeredPaths);
        }

        internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
        {
            string RouteKey(string method, string path) => $"{method}:{path}";
            
            if (registeredPaths.Add(RouteKey("POST", "/register")))
            {
                group.MapPost("/register", async (
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

            if (registeredPaths.Add(RouteKey("DELETE", "/{webhookId}")))
            {
                group.MapDelete("/{webhookId}", async (
                    string webhookId,
                    IWebhookService webhookService,
                    HttpContext context) =>
                {
                    var result = await webhookService.DeleteWebhookAsync(webhookId);
                    return result ? Results.Ok() : Results.NotFound();
                })
                .WithName($"{version} - DeleteWebhook")
                .WithDescription("Delete an existing webhook");
            }

            if (registeredPaths.Add(RouteKey("GET", "/{webhookId}")))
            {
                group.MapGet("/{webhookId}", async (
                string webhookId,
                IWebhookService webhookService,
                HttpContext context) =>
                {
                    var webhook = await webhookService.GetWebhookAsync(webhookId);
                    return webhook != null ? Results.Ok(webhook) : Results.NotFound();
                })
                .WithName($"{version} - GetWebhook")
                .WithDescription("Get webhook details");
            }

            if (registeredPaths.Add(RouteKey("POST", "/trigger")))
            {
                group.MapPost("/trigger", async (
                WebhookTriggerDto triggerDto,
                IWebhookService webhookService,
                HttpContext context) =>
                {
                    var result = await webhookService.ManuallyTriggerWebhookAsync(triggerDto);
                    
                    if (!result.Success)
                    {
                        return Results.BadRequest(new
                        {
                            success = false,
                            errors = result.Errors
                        });
                    }

                    return Results.Ok(new
                    {
                        success = true,
                        webhooksTriggered = result.WebhooksTriggered,
                        warnings = result.Errors.Any() ? result.Errors : null
                    });
                })
                .WithName($"{version} - TriggerWebhook")
                .WithDescription("Manually trigger webhooks for a specific workflow and event type");
            }
        }
    }
} 