using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Services;
using Shared.Utils.Services; 

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
            [FromQuery] string? activationName,
            [FromServices] IKnowledgeService service) =>
        {
            _logger.LogInformation("Getting latest knowledge for name: {name}, agent: {agent}, activationName: {activationName}", 
                name, agent, activationName);
            
            var result = await service.GetLatestByNameAsync(name, agent, activationName);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Get latest knowledge";
            operation.Description = "Retrieves the most recent knowledge for the specified name and agent. " +
                "If activationName is provided, uses fallback logic: " +
                "1) Try tenantId + agent + activationName, " +
                "2) If not found, try tenantId + agent (any or no activationName), " +
                "3) If not found, try system-scoped with agent.";
            return operation;
        });

        knowledgeGroup.MapGet("/latest/system", async (
            [FromQuery] string name,
            [FromQuery] string agent,
            [FromServices] IKnowledgeService service) =>
        {
            _logger.LogInformation("Getting latest system knowledge for name: {name}, agent: {agent}", name, agent);
            var result = await service.GetLatestSystemByNameAsync(name, agent);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Get latest system knowledge";
            operation.Description = "Retrieves the most recent system-scoped knowledge (no tenant) for the specified name and agent";
            return operation;
        });

        knowledgeGroup.MapPost("/", async (
            [FromBody] KnowledgeCreateRequest request,
            [FromServices] IKnowledgeService endpoint) =>
        {
            _logger.LogInformation("Creating new knowledge with name: {Name}, agent: {Agent}, activationName: {ActivationName}", 
                request.Name, request.Agent, request.ActivationName);
            var knowledgeRequest = new KnowledgeRequest 
            {
                Name = request.Name,
                Content = request.Content,
                Agent = request.Agent,
                Type = request.Type,
                SystemScoped = request.SystemScoped,
                ActivationName = request.ActivationName
            };
            var result = await endpoint.Create(knowledgeRequest);
            return Results.Created($"/api/agent/knowledge/latest?name={request.Name}&agent={request.Agent}", result);
        })
        .Produces<object>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Create knowledge";
            operation.Description = "Creates a new knowledge entity with optional activation name";
            return operation;
        });

        knowledgeGroup.MapDelete("/", async (
            [FromQuery] string name,
            [FromQuery] string agent,
            [FromServices] IKnowledgeService service) =>
        {
            _logger.LogInformation("Deleting knowledge with name: {Name}, agent: {Agent}", name, agent);
            var deleteRequest = new DeleteAllVersionsRequest 
            {
                Name = name,
                Agent = agent
            };
            var result = await service.DeleteAllVersions(deleteRequest);
            return result;
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "Delete knowledge";
            operation.Description = "Deletes all versions of knowledge for the specified name and agent";
            return operation;
        });

        knowledgeGroup.MapGet("/list", async (
            [FromQuery] string agent,
            [FromServices] IKnowledgeService service) =>
        {
            _logger.LogInformation("Listing knowledge for agent: {Agent}", agent);
            var result = await service.GetLatestByAgent(agent);
            return result;
        })
        .Produces<object[]>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => {
            operation.Summary = "List knowledge";
            operation.Description = "Lists all knowledge items for the specified agent";
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
    public bool SystemScoped { get; init; } = false;
    public string? ActivationName { get; init; }
} 