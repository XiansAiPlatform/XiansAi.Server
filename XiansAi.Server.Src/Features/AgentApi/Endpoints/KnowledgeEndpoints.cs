using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

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
            [FromServices] KnowledgeService endpoint) =>
        {
            _logger.LogInformation("Getting latest knowledge for -{name}-", name);
            var result = await endpoint.GetLatestKnowledge(name);
            return result;
        })
        .WithOpenApi(operation => {
            operation.Summary = "Get latest knowledge";
            operation.Description = "Retrieves the most recent knowledge for the specified name";
            return operation;
        });
    }
} 