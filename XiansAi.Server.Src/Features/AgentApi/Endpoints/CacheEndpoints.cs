using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using Features.AgentApi.Endpoints.Models;

namespace Features.AgentApi.Endpoints;


// Non-static class for logger type parameter
public class CacheEndpointLogger {}

public static class CacheEndpoints
{
    private static ILogger<CacheEndpointLogger> _logger = null!;

    public static void MapCacheEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CacheEndpointLogger>();
        
        // Map cache endpoints
        var cacheGroup = app.MapGroup("/api/agent/cache")
            .WithTags("AgentAPI - Cache")
            .RequiresCertificate();
            
        cacheGroup.MapPost("/get", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.GetValue(request.Key);
        })
        .WithName("Get Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Get a value from cache";
            operation.Description = "Retrieves a value from the cache by its key. Returns the cached value if found, otherwise returns null.";
            return operation;
        });

        cacheGroup.MapPost("/set", async (
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
        .WithName("Set Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Set a value in cache";
            operation.Description = "Stores a value in the cache with the specified key and optional expiration settings";
            return operation;
        });

        cacheGroup.MapPost("/delete", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.DeleteValue(request.Key);
        })
        .WithName("Delete Cache Value")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a value from cache";
            operation.Description = "Removes a value from the cache by its key";
            return operation;
        });
    }
} 