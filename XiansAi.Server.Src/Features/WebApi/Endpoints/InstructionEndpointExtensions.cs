using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class InstructionEndpointExtensions
{
    public static void MapInstructionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructionById(id);
        })
        .WithName("Get Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructions();
        })
        .WithName("Get Instructions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapPost("/api/client/instructions", async (
            [FromBody] InstructionRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.CreateInstruction(request);
        })
        .WithName("Create Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest/{name}", async (
            string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructionByName(name);
        })
        .WithName("Get Latest Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructions();
        })
        .WithName("Get Latest Instructions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteInstruction(id);
        })
        .WithName("Delete Instruction")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        app.MapGet("/api/client/instructions/versions", async (
            [FromQuery]string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();
    }
} 