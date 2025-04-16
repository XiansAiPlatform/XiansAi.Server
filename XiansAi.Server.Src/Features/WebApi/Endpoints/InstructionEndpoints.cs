using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Services;
using Features.WebApi.Auth;

namespace Features.WebApi.Endpoints;

public static class InstructionEndpoints
{
    public static void MapInstructionEndpoints(this WebApplication app)
    {
        // Map instruction endpoints with common attributes
        var instructionsGroup = app.MapGroup("/api/client/instructions")
            .WithTags("WebAPI - Instructions")
            .RequiresValidTenant();

        instructionsGroup.MapGet("/{id}", async (
            string id,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructionById(id);
        })
        .WithName("Get Instruction")
        .WithOpenApi();

        instructionsGroup.MapGet("/", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructions();
        })
        .WithName("Get Instructions")
        .WithOpenApi();

        instructionsGroup.MapPost("/", async (
            [FromBody] InstructionRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.CreateInstruction(request);
        })
        .WithName("Create Instruction")
        .WithOpenApi();

        instructionsGroup.MapGet("/latest/{name}", async (
            string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructionByName(name);
        })
        .WithName("Get Latest Instruction")
        .WithOpenApi();

        instructionsGroup.MapGet("/latest", async (
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetLatestInstructions();
        })
        .WithName("Get Latest Instructions")
        .WithOpenApi();

        instructionsGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteInstruction(id);
        })
        .WithName("Delete Instruction")
        .WithOpenApi();

        instructionsGroup.MapDelete("/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .WithOpenApi();

        instructionsGroup.MapGet("/versions", async (
            [FromQuery]string name,
            [FromServices] InstructionsService endpoint) =>
        {
            return await endpoint.GetInstructionVersions(name);
        })
        .WithName("Get Instruction Versions")
        .WithOpenApi();
    }
} 