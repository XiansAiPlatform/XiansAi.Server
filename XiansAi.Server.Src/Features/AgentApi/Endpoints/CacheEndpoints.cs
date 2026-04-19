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
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Get a value from cache")
        .WithDescription("Retrieves a value from the cache by its key. Returns the cached value if found, otherwise returns null.");

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
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Set a value in cache")
        .WithDescription("Stores a value in the cache with the specified key and optional expiration settings");

        cacheGroup.MapPost("/delete", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] IObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.DeleteValue(request.Key);
        })
        .WithName("Delete Cache Value")
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Delete a value from cache")
        .WithDescription("Removes a value from the cache by its key");
    }
} 