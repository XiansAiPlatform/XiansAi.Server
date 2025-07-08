using Shared.Services; // For IWebhookService, WebhookCreateRequest, WebhookUpdateRequest
//using XiansAi.Server.Features.AgentApi.Models; // For WebhookTriggerDto
namespace Features.UserApi.Webhooks
{
    public static class WebhookTriggerEndpoints
    {
        public static void MapWebhookTriggerEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // WebAPI endpoints
            var webhooksGroup = endpoints.MapGroup("/api/webhooks")
                .WithTags("UserAPI - Webhooks")
                .RequireAuthorization("WebhookAuthPolicy");

            webhooksGroup.MapPost("/trigger", async (
                WebhookTriggerDto triggerDto,
                IWebhookService webhookService,
                IMessageService messageService,
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
