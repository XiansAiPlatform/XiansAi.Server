using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class MessagingEndpoints
{
    public static void MapMessagingEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var messagingGroup = app.MapGroup("/api/client/messaging")
            .WithTags("WebAPI - Messaging")
            .RequiresValidTenant();

        messagingGroup.MapGet("/agents", async (
            [FromServices] IMessagingService endpoint) =>
        {
            return await endpoint.GetGroupedDefinitions();
        })
        .WithName("Get Grouped Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get grouped definitions";
            operation.Description = "Retrieves grouped definitions";
            return operation;
        });

        messagingGroup.MapGet("/workflows", async (
            [FromQuery] string? agentName,
            [FromQuery] string? typeName,
            [FromServices] IMessagingService endpoint) =>
        {
            return await endpoint.GetWorkflowInstances(agentName, typeName);
        })
        .WithName("Get Workflow Instances")
        .WithOpenApi(operation => {
            operation.Summary = "Get workflow instances";
            operation.Description = "Retrieves workflow instances";
            return operation;
        });        
    }
} 