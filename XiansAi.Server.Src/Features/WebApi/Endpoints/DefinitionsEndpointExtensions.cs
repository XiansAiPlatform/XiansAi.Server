using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class DefinitionsEndpointExtensions
{
    public static void MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapDelete("/api/client/definitions/{definitionId}", async (
            string definitionId,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.DeleteDefinition(definitionId);
        })
        .WithName("Delete Definition")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a specific definition";
            operation.Description = "Removes a definition by its ID";
            return operation;
        });

        app.MapGet("/api/client/definitions", async (
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.GetLatestDefinitions(startTime, endTime, owner);
        })
        .WithName("Get Latest Definitions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi(operation => {
            operation.Summary = "Get definitions with optional filters";
            operation.Description = "Retrieves definitions with optional filtering by date range and owner";
            return operation;
        });
    }
} 