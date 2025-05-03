using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using XiansAi.Server.Shared.Services;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class KnowledgeEndpointLogger {}

public static class KnowledgeEndpoints
{
    private static ILogger<KnowledgeEndpointLogger> _logger = null!;

    public static void MapKnowledgeEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<KnowledgeEndpointLogger>();
        
        // Map knowledge endpoints
        var knowledgeGroup = app.MapGroup("/api/agent/knowledge")
            .WithTags("AgentAPI - Knowledge")
            .RequiresCertificate();
            
        knowledgeGroup.MapGet("/latest", async (
            [FromQuery] string name,
            [FromQuery] string agent,
            [FromServices] IKnowledgeService endpoint) =>
        {
            _logger.LogInformation("Getting latest knowledge for name: {name}, agent: {agent}", name, agent);
            return await endpoint.GetLatestByName(name, agent);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Get latest knowledge";
            operation.Description = "Retrieves the most recent knowledge for the specified name and agent";
            return operation;
        });

        knowledgeGroup.MapPost("/", async (
            [FromBody] KnowledgeCreateRequest request,
            [FromServices] IKnowledgeService endpoint) =>
        {
            _logger.LogInformation("Creating new knowledge with name: {Name}, agent: {Agent}", request.Name, request.Agent);
            var knowledgeRequest = new KnowledgeRequest 
            {
                Name = request.Name,
                Content = request.Content,
                Agent = request.Agent,
                Type = request.Type
            };
            var result = await endpoint.Create(knowledgeRequest);
            return Results.Created($"/api/agent/knowledge/latest?name={request.Name}&agent={request.Agent}", result);
        })
        .WithOpenApi(operation => {
            operation.Summary = "Create knowledge";
            operation.Description = "Creates a new knowledge entity";
            return operation;
        });
    }
} 

public record KnowledgeCreateRequest
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public required string Agent { get; init; }
    public required string Type { get; init; }
} 