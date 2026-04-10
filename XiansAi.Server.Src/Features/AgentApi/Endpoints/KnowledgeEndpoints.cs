using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
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
            [FromServices] IKnowledgeService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId ?? "(null)";
            _logger.LogInformation(
                "Agent knowledge/latest request: name={Name}, agent={Agent}, activationName={ActivationName}, tenantId={TenantId}",
                name, agent, activationName ?? "(null)", tenantId);
            Console.WriteLine($"[Agent API] knowledge/latest: name={name}, agent={agent}, activationName={activationName ?? "(null)"}, tenantId={tenantId}");

            var result = await service.GetLatestByNameAsync(name, agent, activationName);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Get latest knowledge")
        .WithDescription("Retrieves the most recent knowledge for the specified name and agent. ");

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
        
        .WithSummary("Get latest system knowledge")
        .WithDescription("Retrieves the most recent system-scoped knowledge (no tenant) for the specified name and agent");

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
                ActivationName = request.ActivationName,
                Description = request.Description,
                Visible = request.Visible
            };
            var result = await endpoint.Create(knowledgeRequest);
            return Results.Created($"/api/agent/knowledge/latest?name={request.Name}&agent={request.Agent}", result);
        })
        .Produces<object>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Create knowledge")
        .WithDescription("Creates a new knowledge entity with optional activation name");

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
        
        .WithSummary("Delete knowledge")
        .WithDescription("Deletes all versions of knowledge for the specified name and agent");

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
        
        .WithSummary("List knowledge")
        .WithDescription("Lists all knowledge items for the specified agent");
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
    public string? Description { get; init; }
    public bool Visible { get; init; } = true;
} 