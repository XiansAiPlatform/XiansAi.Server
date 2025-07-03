using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Features.WebApi.Auth;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        // Map webhook endpoints with common attributes
        var webhooksGroup = app.MapGroup("/api/client/webhooks")
            .WithTags("WebAPI - Webhooks")
            .RequiresValidTenant();

        webhooksGroup.MapGet("/", async (
            [FromServices] IWebhookService endpoint) =>
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

        webhooksGroup.MapGet("/workflow/{workflowId}", async (
            string workflowId,
            [FromServices] IWebhookService endpoint) =>
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

        webhooksGroup.MapGet("/{webhookId}", async (
            string webhookId,
            [FromServices] IWebhookService endpoint) =>
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

        webhooksGroup.MapPost("/", async (
            [FromBody] WebhookCreateRequest request,
            [FromServices] IWebhookService endpoint) =>
        {
            // Validate the model manually
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

        webhooksGroup.MapPut("/{webhookId}", async (
            string webhookId,
            [FromBody] WebhookUpdateRequest request,
            [FromServices] IWebhookService endpoint) =>
        {
            // Validate the model manually for update requests
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

        webhooksGroup.MapDelete("/{webhookId}", async (
            string webhookId,
            [FromServices] IWebhookService endpoint) =>
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
    }
} 