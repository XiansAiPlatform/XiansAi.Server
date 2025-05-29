using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using XiansAi.Server.Shared.Services;

namespace Features.WebApi.Endpoints;

public static class KnowledgeEndpoints
{
    public static void MapKnowledgeEndpoints(this WebApplication app)
    {
        // Map instruction endpoints with common attributes
        var knowledgeGroup = app.MapGroup("/api/client/knowledge")
            .WithTags("WebAPI - Knowledge")
            .RequiresValidTenant();

        knowledgeGroup.MapGet("/latest/all", async (
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.GetLatestAll();
        })
        .WithName("Get Latest Instructions")
        .WithOpenApi();
        
        knowledgeGroup.MapGet("/{id}", async (
            string id,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.GetById(id);
        })
        .WithName("Get Instruction")
        .WithOpenApi();

        knowledgeGroup.MapPost("/", async (
            [FromBody] KnowledgeRequest request,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.Create(request);
        })
        .WithName("Create Instruction")
        .WithOpenApi();

        knowledgeGroup.MapGet("/latest", async (
            [FromQuery] string name,
            [FromQuery] string agent,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.GetLatestByName(name, agent);
        })
        .WithName("Get Latest Instruction")
        .WithOpenApi();

        knowledgeGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.DeleteById(id);
        })
        .WithName("Delete Instruction")
        .WithOpenApi();

        knowledgeGroup.MapDelete("/all", async (
            [FromBody] DeleteAllVersionsRequest request,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.DeleteAllVersions(request);
        })
        .WithName("Delete All Versions")
        .WithOpenApi();

        knowledgeGroup.MapGet("/versions", async (
            [FromQuery] string name,
            [FromQuery] string? agent,
            [FromServices] IKnowledgeService endpoint) =>
        {
            return await endpoint.GetVersions(name, agent);
        })
        .WithName("Get Knowledge Versions")
        .WithOpenApi();
    }
} 