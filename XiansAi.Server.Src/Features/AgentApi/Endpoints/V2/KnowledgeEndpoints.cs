using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using XiansAi.Server.Shared.Services;

//Boilerplate code for future versions

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

        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MapRoutes(knowledgeGroup, version, registeredPaths);

        // Reuse v1 mappings
        V1.KnowledgeEndpointsV1.MapRoutes(knowledgeGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        // string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var latestPath = "/latest";
        // if (registeredPaths.Add(RouteKey("GET", latestPath)))
        // {
        //     group.MapGet(latestPath, async (
        //         [FromQuery] string name,
        //         [FromQuery] string agent,
        //         [FromServices] IKnowledgeService endpoint) =>
        //     {
        //         _logger.LogInformation("Getting latest knowledge for name: {name}, agent: {agent}", name, agent);
        //         return await endpoint.GetLatestByName(name, agent);
        //     })
        //     .WithOpenApi(operation =>
        //     {
        //         operation.Summary = "Get latest knowledge";
        //         operation.Description = "Retrieves the most recent knowledge for the specified name and agent";
        //         return operation;
        //     });
        // }
    }
}