using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

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
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetAllWebhooks();
        })
        .WithName("Get All Webhooks")
        .WithOpenApi(operation => {
            operation.Summary = "Get all webhooks for the current tenant";
            operation.Description = "Retrieves all webhooks associated with the current tenant";
            return operation;
        });

        webhooksGroup.MapGet("/workflow/{workflowId}", async (
            string workflowId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetWebhooksByWorkflow(workflowId);
        })
        .WithName("Get Webhooks By Workflow")
        .WithOpenApi(operation => {
            operation.Summary = "Get webhooks for a specific workflow";
            operation.Description = "Retrieves all webhooks associated with the specified workflow";
            return operation;
        });

        webhooksGroup.MapGet("/{webhookId}", async (
            string webhookId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetWebhook(webhookId);
        })
        .WithName("Get Webhook")
        .WithOpenApi(operation => {
            operation.Summary = "Get a specific webhook";
            operation.Description = "Retrieves details of a specific webhook by ID";
            return operation;
        });

        webhooksGroup.MapPost("/", async (
            [FromBody] WebhookCreateRequest request,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.CreateWebhook(request);
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
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.UpdateWebhook(webhookId, request);
        })
        .WithName("Update Webhook")
        .WithOpenApi(operation => {
            operation.Summary = "Update an existing webhook";
            operation.Description = "Updates an existing webhook with new information";
            return operation;
        });

        webhooksGroup.MapDelete("/{webhookId}", async (
            string webhookId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.DeleteWebhook(webhookId);
        })
        .WithName("Delete Webhook")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a webhook";
            operation.Description = "Deletes an existing webhook";
            return operation;
        });
    }
} 