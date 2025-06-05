using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using XiansAi.Server.Shared.Services;

namespace Features.AgentApi.Endpoints.V2;

public class KnowledgeEndpointLogger {}

public static class KnowledgeEndpointsV2
{
    private static ILogger<KnowledgeEndpointLogger> _logger = null!;

    public static void MapKnowledgeEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        _logger = loggerFactory.CreateLogger<KnowledgeEndpointLogger>();
        
        // Map knowledge endpoints
        var knowledgeGroup = app.MapGroup($"/api/{version}/agent/knowledge")
            .WithTags($"AgentAPI - Knowledge {version}")
            .RequiresCertificate();

        // Reuse v1 mappings
        V1.KnowledgeEndpointsV1.CommonMapRoutes(knowledgeGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(knowledgeGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
    }
}