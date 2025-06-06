using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Endpoints.Models;

//Boilerplate code for future versions

namespace Features.AgentApi.Endpoints.V2;

public class CacheEndpointLogger { }

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
        var registeredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        MapRoutes(cacheGroup, version, registeredPaths);
        V1.CacheEndpointsV1.MapRoutes(cacheGroup, version, registeredPaths);
    }

    internal static void MapRoutes(RouteGroupBuilder group, string version, HashSet<string> registeredPaths = null!)
    {
        string RouteKey(string method, string path) => $"{method}:{path}";

        // If v2 has the same endpoint with changes, we can overwrite it, before v1 is called this method will be called and hashset will record that it is already called
        // Hence v1 would not register the same endpoint again

        // var getPath = "/get";
        // if (registeredPaths.Add(RouteKey("POST", getPath)))
        // {
        //     group.MapPost(getPath, async (
        //     [FromBody] CacheKeyRequest request,
        //     [FromServices] IObjectCacheWrapperService endpoint) =>
        //     {
        //         return await endpoint.GetValue(request.Key);
        //     })
        //     .WithName($"{version} - Get Cache Value")
        //     .WithOpenApi(operation => {
        //         operation.Summary = "Get a value from cache";
        //         operation.Description = "Retrieves a value from the cache by its key. Returns the cached value if found, otherwise returns null.";
        //         return operation;
        //     });
        // }
    }
} 