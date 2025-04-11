using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

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
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/instructions", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructions();
        })
        .WithName("Get Instructions")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapPost("/api/client/instructions", async (
            [FromBody] InstructionRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.CreateInstruction(request);
        })
        .WithName("Create Instruction")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest/{name}", async (
            string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructionByName(name);
        })
        .WithName("Get Latest Instruction")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/instructions/latest", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructions();
        })
        .WithName("Get Latest Instructions")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/{id}", async (
            string id,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteInstruction(id);
        })
        .WithName("Delete Instruction")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapDelete("/api/client/instructions/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .RequiresValidTenant()
        .WithOpenApi();

        app.MapGet("/api/client/instructions/versions", async (
            [FromQuery]string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .RequiresValidTenant()
        .WithOpenApi();
    }
} 