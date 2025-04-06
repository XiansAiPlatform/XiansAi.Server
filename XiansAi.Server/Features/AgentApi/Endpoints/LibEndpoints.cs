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
        MapKnowledgeEndpoints(app);
        MapActivityHistoryEndpoints(app);
        MapDefinitionsEndpoints(app);
    }

    private static void MapObjectCacheEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/cache/get", async (
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

        app.MapPost("/api/agent/cache/set", async (
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

        app.MapPost("/api/agent/cache/delete", async (
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
    
    private static void MapKnowledgeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/agent/knowledge/latest", async (
            [FromQuery] string name,
            [FromServices] KnowledgeService endpoint) =>
        {
            var result = await endpoint.GetLatestKnowledge(name);
            return result;
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Get latest knowledge";
            operation.Description = "Retrieves the most recent knowledge for the specified name";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Knowledge" }};
            
            return operation;
        });
    }

    private static void MapActivityHistoryEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/activity-history", (
            [FromServices] ActivityHistoryService endpoint,
            [FromBody] ActivityHistoryRequest request,
            HttpContext context) =>
        {
            try
            {
                endpoint.Create(request);
                return Results.Ok("Activity history creation queued");
            }
            catch (JsonException)
            {
                return Results.BadRequest("Invalid JSON format");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error creating activity history: {ex.Message}");
            }
        })
        .RequiresCertificate()
        .WithOpenApi(operation => {
            operation.Summary = "Create activity history";
            operation.Description = "Creates a new activity history record in the system";
            operation.Parameters.Add(OpenAPIUtils.CertificateParameter());
            operation.Tags = new List<OpenApiTag> { new() { Name = "AgentAPI - Activity History" }};
            
            return operation;
        });
    }

    private static void MapDefinitionsEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/definitions", async (
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