using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class DefinitionsEndpointExtensions
{
    public static void MapDefinitionsEndpoints(this WebApplication app)
    {
        // Map definitions endpoints with common attributes
        var definitionsGroup = app.MapGroup("/api/client/definitions")
            .WithTags("WebAPI - Definitions")
            .RequiresValidTenant();

        definitionsGroup.MapDelete("/{definitionId}", async (
            string definitionId,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.DeleteDefinition(definitionId);
        })
        .WithName("Delete Definition")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a specific definition";
            operation.Description = "Removes a definition by its ID";
            return operation;
        });

        definitionsGroup.MapGet("/", async (
            [FromQuery] DateTime? startTime,
            [FromQuery] DateTime? endTime,
            [FromQuery] string? owner,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.GetLatestDefinitions(startTime, endTime, owner);
        })
        .WithName("Get Latest Definitions")
        .WithOpenApi(operation => {
            operation.Summary = "Get definitions with optional filters";
            operation.Description = "Retrieves definitions with optional filtering by date range and owner";
            return operation;
        });
    }
} 