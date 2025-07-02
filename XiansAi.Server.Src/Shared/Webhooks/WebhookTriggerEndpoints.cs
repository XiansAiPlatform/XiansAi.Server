using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shared.Services; // For IWebhookService, WebhookCreateRequest, WebhookUpdateRequest
//using XiansAi.Server.Features.AgentApi.Models; // For WebhookTriggerDto
using Shared.Utils.Services; // For ToHttpResult extension methods
using System.Text.Json;
using Shared.Repositories;

// These interfaces and DTOs should be defined elsewhere in your project.
// Here, we just reference them as in the original endpoints.
// using Features.WebApi.Services;
// using XiansAi.Server.Features.AgentApi.Models;

namespace XiansAi.Server.Shared.Webhooks
{
    public static class WebhookTriggerEndpoints
    {
        public static void MapWebhookTriggerEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // WebAPI endpoints
            var webhooksGroup = endpoints.MapGroup("/api/webhooks")
                .WithTags("WebAPI - Webhooks")
                .RequireAuthorization("WebhookAuthPolicy");

            webhooksGroup.MapGet("/", async (IWebhookService endpoint) =>
            {
                var result = await endpoint.GetAllWebhooks();
                return result.ToHttpResult();
            })
            .WithName("Get All Webhooks")
            .WithOpenApi(operation => {
                operation.Summary = "Get all webhooks for the current tenant";
                operation.Description = "Retrieves all webhooks associated with the current tenant";
                return operation;
            });

            webhooksGroup.MapGet("/workflow/{workflowId}", async (string workflowId, IWebhookService endpoint) =>
            {
                var result = await endpoint.GetWebhooksByWorkflow(workflowId);
                return result.ToHttpResult();
            })
            .WithName("Get Webhooks By Workflow")
            .WithOpenApi(operation => {
                operation.Summary = "Get webhooks for a specific workflow";
                operation.Description = "Retrieves all webhooks associated with the specified workflow";
                return operation;
            });

            webhooksGroup.MapGet("/{webhookId}", async (string webhookId, IWebhookService endpoint) =>
            {
                var result = await endpoint.GetWebhook(webhookId);
                return result.ToHttpResult();
            })
            .WithName("Get Webhook")
            .WithOpenApi(operation => {
                operation.Summary = "Get a specific webhook";
                operation.Description = "Retrieves details of a specific webhook by ID";
                return operation;
            });

            webhooksGroup.MapPost("/", async (WebhookCreateRequest request, IWebhookService endpoint) =>
            {
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(request);
                if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => vr.ErrorMessage).ToList();
                    return Results.BadRequest(new
                    {
                        error = "Validation failed",
                        errors = errors
                    });
                }
                var result = await endpoint.CreateWebhook(request);
                if (result.IsSuccess && result.Data != null)
                {
                    return Results.Created(result.Data.Location, result.Data.Webhook);
                }
                return result.ToHttpResult();
            })
            .WithName("Create Webhook")
            .WithOpenApi(operation => {
                operation.Summary = "Create a new webhook";
                operation.Description = "Creates a new webhook for a specific workflow";
                return operation;
            });

            webhooksGroup.MapPut("/{webhookId}", async (string webhookId, WebhookUpdateRequest request, IWebhookService endpoint) =>
            {
                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(request);
                if (!Validator.TryValidateObject(request, validationContext, validationResults, true))
                {
                    var errors = validationResults.Select(vr => vr.ErrorMessage).ToList();
                    return Results.BadRequest(new
                    {
                        error = "Validation failed",
                        errors = errors
                    });
                }
                var result = await endpoint.UpdateWebhook(webhookId, request);
                return result.ToHttpResult();
            })
            .WithName("Update Webhook")
            .WithOpenApi(operation => {
                operation.Summary = "Update an existing webhook";
                operation.Description = "Updates an existing webhook with new information";
                return operation;
            });

            webhooksGroup.MapDelete("/{webhookId}", async (string webhookId, IWebhookService endpoint) =>
            {
                var result = await endpoint.DeleteWebhook(webhookId);
                if (result.IsSuccess)
                {
                    return Results.NoContent();
                }
                return result.ToHttpResult();
            })
            .WithName("Delete Webhook")
            .WithOpenApi(operation => {
                operation.Summary = "Delete a webhook";
                operation.Description = "Deletes an existing webhook";
                return operation;
            });

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
