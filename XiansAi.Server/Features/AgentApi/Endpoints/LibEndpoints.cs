using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services.Lib;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;

namespace Features.AgentApi.Endpoints;

// Request models for cache operations
public class CacheKeyRequest
{
    public string Key { get; set; } = string.Empty;
}

public class CacheSetRequest
{
    public string Key { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public int? RelativeExpirationMinutes { get; set; }
    public int? SlidingExpirationMinutes { get; set; }
}

public static class LibEndpoints
{
    public static void MapLibEndpoints(this WebApplication app)
    {
        MapObjectCacheEndpoints(app);
        MapInstructionsEndpoints(app);
        MapActivitiesEndpoints(app);
        MapDefinitionsEndpoints(app);
    }

    private static void MapObjectCacheEndpoints(this WebApplication app)
    {
        app.MapPost("/api/client/cache/get", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.GetValue(request.Key);
        })
        .WithName("Get Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get a value from cache";
            operation.Description = "Retrieves a value from the cache by its key. Returns the cached value if found, otherwise returns null.";
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Cache" }};
            // Add header parameters to OpenAPI documentation
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            return operation;
        });

        app.MapPost("/api/client/cache/set", async (
            [FromBody] CacheSetRequest request,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            var options = new CacheOptions
            {
                RelativeExpirationMinutes = request.RelativeExpirationMinutes,
                SlidingExpirationMinutes = request.SlidingExpirationMinutes
            };
            
            return await endpoint.SetValue(request.Key, request.Value, options);
        })
        .WithName("Set Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Set a value in cache";
            operation.Description = "Stores a value in the cache with the specified key and optional expiration settings";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Cache" }};
            
            return operation;
        });

        app.MapPost("/api/client/cache/delete", async (
            [FromBody] CacheKeyRequest request,
            [FromServices] ObjectCacheWrapperService endpoint) =>
        {
            return await endpoint.DeleteValue(request.Key);
        })
        .WithName("Delete Cache Value")
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Delete a value from cache";
            operation.Description = "Removes a value from the cache by its key";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Cache" }};
            
            return operation;
        });
    }
    
    private static void MapInstructionsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/server/instructions/latest", async (
            [FromQuery] string name,
            [FromServices] InstructionsService endpoint) =>
        {
            var result = await endpoint.GetLatestInstruction(name);
            return result;
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get latest instructions";
            operation.Description = "Retrieves the most recent instructions for the specified name";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Instructions" }};
            
            return operation;
        });
    }

    private static void MapActivitiesEndpoints(this WebApplication app)
    {
        app.MapPost("/api/server/activities", async (
            [FromBody] ActivityHistoryRequest request,
            [FromServices] ActivityHistoryService endpoint) =>
        {
            await endpoint.CreateAsync(request);
            return Results.Ok();
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Create activity history";
            operation.Description = "Creates a new activity history record in the system";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Activities" }};
            
            return operation;
        });
    }

    private static void MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/server/definitions", async (
            [FromBody] FlowDefinitionRequest request,
            [FromServices] DefinitionsService endpoint) =>
        {
            return await endpoint.CreateAsync(request);
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Create flow definition";
            operation.Description = "Creates a new flow definition in the system";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Definitions" }};
            
            return operation;
        });
    }
}