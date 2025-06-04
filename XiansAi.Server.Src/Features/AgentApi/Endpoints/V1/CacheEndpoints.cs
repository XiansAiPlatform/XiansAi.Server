using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Endpoints.Models;

namespace Features.AgentApi.Endpoints.V1;


// Non-static class for logger type parameter
public class CacheEndpointLogger {}

public static class CacheEndpointsV1
{
    private static ILogger<CacheEndpointLogger> _logger = null!;

    public static void MapCacheEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        var version = "v1";
        _logger = loggerFactory.CreateLogger<CacheEndpointLogger>();
        
        // Map cache endpoints
        var cacheGroup = app.MapGroup($"/api/{version}/agent/cache")
            .WithTags($"AgentAPI - Cache {version}")
            .RequiresCertificate();
        
        // If there are any routes that are common for multiple versions, add them here
        CommonMapRoutes(cacheGroup, version);

        // If there are any routes that will be deleted in future versions, add them here
        UniqueMapRoutes(cacheGroup, version);
    }

    internal static void CommonMapRoutes(RouteGroupBuilder group, string version)
    {
        // If there are any routes that are common for multiple versions, add them here
    }

    internal static void UniqueMapRoutes(RouteGroupBuilder group, string version)
    {
            
        group.MapPost("/get", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.GetValue(request.Key);
        })
        .WithName($"{version} - Get Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Get a value from cache";
            operation.Description = "Retrieves a value from the cache by its key. Returns the cached value if found, otherwise returns null.";
            return operation;
        });

        group.MapPost("/set", async (
            [FromBody] CacheSetRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            var options = new CacheOptions
            {
                RelativeExpirationMinutes = request.RelativeExpirationMinutes,
                SlidingExpirationMinutes = request.SlidingExpirationMinutes
            };
            
            return await endpoint.SetValue(request.Key, request.Value, options);
        })
        .WithName($"{version} - Set Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Set a value in cache";
            operation.Description = "Stores a value in the cache with the specified key and optional expiration settings";
            return operation;
        });

        group.MapPost("/delete", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.DeleteValue(request.Key);
        })
        .WithName($"{version} - Delete Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a value from cache";
            operation.Description = "Removes a value from the cache by its key";
            return operation;
        });
    }
} 