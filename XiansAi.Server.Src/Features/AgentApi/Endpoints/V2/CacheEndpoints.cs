using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Endpoints.Models;

namespace Features.AgentApi.Endpoints.V2;

public class CacheEndpointLogger {}

public static class CacheEndpointsV2
{
    private static ILogger<CacheEndpointLogger> _logger = null!;

    public static void MapCacheEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v2";
        _logger = loggerFactory.CreateLogger<CacheEndpointLogger>();
        
        // Map cache endpoints
        var cacheGroup = app.MapGroup($"/api/{version}/agent/cache")
            .WithTags($"AgentAPI - Cache {version}")
            .RequiresCertificate();
        
        // Reuse v1 mappings
        V1.CacheEndpointsV1.CommonMapRoutes(cacheGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(cacheGroup, version);
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
        // You can add new routes specific to v2 here if needed
    }
} 