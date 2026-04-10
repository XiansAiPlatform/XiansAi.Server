using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Features.AgentApi.Services;
using Features.AgentApi.Models;
using System.Text.Json;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class DocumentEndpointLogger {}

/// <summary>
/// Provides extension methods for registering document storage API endpoints.
/// </summary>
public static class DocumentEndpoints
{
    private static ILogger<DocumentEndpointLogger> _logger = null!;

    /// <summary>
    /// Maps all document-related endpoints to the application's request pipeline.
    /// </summary>
    /// <param name="app">The web application to add endpoints to.</param>
    /// <param name="loggerFactory">The logger factory to create a logger for the document endpoints.</param>
    public static void MapDocumentEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DocumentEndpointLogger>();
        
        // Map document endpoints
        var documentGroup = app.MapGroup("/api/agent/documents")
            .WithTags("AgentAPI - Documents")
            .RequiresCertificate();

        documentGroup.MapPost("/save", async (
            [FromBody] DocumentRequest<JsonElement> request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Saving document");
            var result = await service.SaveAsync(request);
            return result.ToHttpResult();
        })
        .Produces<string>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Save document")
        .WithDescription("Creates or updates a document with optional TTL and overwrite settings");

        documentGroup.MapPost("/get", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Getting document with ID: {Id}", request.Id);
            var result = await service.GetAsync(request.Id);
            return result.ToHttpResult();
        })
        .Produces<JsonElement>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Get document by ID")
        .WithDescription("Retrieves a document by its unique identifier");

        documentGroup.MapPost("/get-by-key", async (
            [FromBody] DocumentKeyRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Getting document with Type: {Type} and Key: {Key}", request.Type, request.Key);
            var result = await service.GetByKeyAsync(request.Type, request.Key);
            return result.ToHttpResult();
        })
        .Produces<JsonElement>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Get document by type and key")
        .WithDescription("Retrieves a document by its type and custom key combination");

        documentGroup.MapPost("/query", async (
            [FromBody] DocumentQueryRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Querying documents");
            var result = await service.QueryAsync(request);
            return result.ToHttpResult();
        })
        .Produces<object>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Query documents")
        .WithDescription("Searches for documents based on provided criteria with pagination support");

        documentGroup.MapPost("/update", async (
            [FromBody] DocumentDto<JsonElement> document,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Updating document");
            var result = await service.UpdateAsync(document);
            return result.ToHttpResult();
        })
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Update document")
        .WithDescription("Updates an existing document");

        documentGroup.MapPost("/delete", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Deleting document with ID: {Id}", request.Id);
            var result = await service.DeleteAsync(request.Id);
            return result.ToHttpResult();
        })
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Delete document")
        .WithDescription("Deletes a document by its unique identifier");

        documentGroup.MapPost("/delete-many", async (
            [FromBody] DocumentIdsRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Deleting multiple documents");
            var result = await service.DeleteManyAsync(request.Ids);
            return result.ToHttpResult();
        })
        .Produces<int>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Delete multiple documents")
        .WithDescription("Deletes multiple documents by their unique identifiers");

        documentGroup.MapPost("/exists", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Checking existence of document with ID: {Id}", request.Id);
            var result = await service.ExistsAsync(request.Id);
            return result.ToHttpResult();
        })
        .Produces<bool>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        
        .WithSummary("Check document existence")
        .WithDescription("Checks if a document exists by its unique identifier");
    }
}
