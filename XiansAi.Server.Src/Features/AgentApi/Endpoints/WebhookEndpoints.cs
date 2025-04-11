using XiansAi.Server.Features.AgentApi.Models;
using XiansAi.Server.Features.AgentApi.Services.Agent;
using Features.AgentApi.Auth;

namespace XiansAi.Server.Features.AgentApi.Endpoints
{
    public static class WebhookEndpoints
    {
        public static void MapWebhookEndpoints(this IEndpointRouteBuilder endpoints)
        {
            var group = endpoints.MapGroup("/api/agent/webhooks")
                .WithTags("Webhooks")
                .RequiresCertificate();

            group.MapPost("/register", async (
                WebhookRegistrationDto registration,
                IWebhookService webhookService,
                HttpContext context) =>
            {
                var webhook = await webhookService.RegisterWebhookAsync(registration);
                return Results.Ok(webhook);
            })
            .WithName("RegisterWebhook")
            .WithDescription("Register a new webhook for a workflow");

            group.MapDelete("/{webhookId}", async (
                Guid webhookId,
                IWebhookService webhookService,
                HttpContext context) =>
            {
                var result = await webhookService.DeleteWebhookAsync(webhookId);
                return result ? Results.Ok() : Results.NotFound();
            })
            .WithName("DeleteWebhook")
            .WithDescription("Delete an existing webhook");

            group.MapGet("/{webhookId}", async (
                Guid webhookId,
                IWebhookService webhookService,
                HttpContext context) =>
            {
                var webhook = await webhookService.GetWebhookAsync(webhookId);
                return webhook != null ? Results.Ok(webhook) : Results.NotFound();
            })
            .WithName("GetWebhook")
            .WithDescription("Get webhook details");

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
            .WithName("TriggerWebhook")
            .WithDescription("Manually trigger webhooks for a specific workflow and event type");
        }
    }
} 