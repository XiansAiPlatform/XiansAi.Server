using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class WebhookEndpointExtensions
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/webhooks", async (
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetAllWebhooks();
        })
        .WithName("Get All Webhooks")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Get all webhooks for the current tenant";
            operation.Description = "Retrieves all webhooks associated with the current tenant";
            return operation;
        });

        app.MapGet("/api/client/webhooks/workflow/{workflowId}", async (
            string workflowId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetWebhooksByWorkflow(workflowId);
        })
        .WithName("Get Webhooks By Workflow")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Get webhooks for a specific workflow";
            operation.Description = "Retrieves all webhooks associated with the specified workflow";
            return operation;
        });

        app.MapGet("/api/client/webhooks/{webhookId}", async (
            string webhookId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.GetWebhook(webhookId);
        })
        .WithName("Get Webhook")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Get a specific webhook";
            operation.Description = "Retrieves details of a specific webhook by ID";
            return operation;
        });

        app.MapPost("/api/client/webhooks", async (
            [FromBody] WebhookCreateRequest request,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.CreateWebhook(request);
        })
        .WithName("Create Webhook")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Create a new webhook";
            operation.Description = "Creates a new webhook for a specific workflow";
            return operation;
        });

        app.MapPut("/api/client/webhooks/{webhookId}", async (
            string webhookId,
            [FromBody] WebhookUpdateRequest request,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.UpdateWebhook(webhookId, request);
        })
        .WithName("Update Webhook")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Update an existing webhook";
            operation.Description = "Updates an existing webhook with new information";
            return operation;
        });

        app.MapDelete("/api/client/webhooks/{webhookId}", async (
            string webhookId,
            [FromServices] WebhookService endpoint) =>
        {
            return await endpoint.DeleteWebhook(webhookId);
        })
        .WithName("Delete Webhook")
        .RequiresValidTenant()
        .WithOpenApi(operation => {
            operation.Summary = "Delete a webhook";
            operation.Description = "Deletes an existing webhook";
            return operation;
        });
    }
} 