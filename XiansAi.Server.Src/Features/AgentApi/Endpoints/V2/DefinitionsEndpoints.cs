using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;

namespace Features.AgentApi.Endpoints.V2;

public class DefinitionsEndpointLogger {}

public static class DefinitionsEndpointsV2
{
    private static ILogger<DefinitionsEndpointLogger> _logger = null!;

    public static void MapDefinitionsEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        _logger = loggerFactory.CreateLogger<DefinitionsEndpointLogger>();
        
        // Map definitions endpoints
        var definitionsGroup = app.MapGroup($"/api/{version}/agent/definitions")
            .WithTags($"AgentAPI - Definitions {version}")
            .RequiresCertificate();
        
        // Reuse v1 mappings
        V1.DefinitionsEndpointsV1.CommonMapRoutes(definitionsGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(definitionsGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
        // For now, we are reusing the v1 mappings
    }
} 