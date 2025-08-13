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
            [FromBody] JsonElement requestElement,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Saving document");
            var result = await service.SaveAsync(requestElement);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Save document";
            operation.Description = "Creates or updates a document with optional TTL and overwrite settings";
            return operation;
        });

        documentGroup.MapPost("/get", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Getting document with ID: {Id}", request.Id);
            var result = await service.GetAsync(request.Id);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Get document by ID";
            operation.Description = "Retrieves a document by its unique identifier";
            return operation;
        });

        documentGroup.MapPost("/get-by-key", async (
            [FromBody] DocumentKeyRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Getting document with Type: {Type} and Key: {Key}", request.Type, request.Key);
            var result = await service.GetByKeyAsync(request.Type, request.Key);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Get document by type and key";
            operation.Description = "Retrieves a document by its type and custom key combination";
            return operation;
        });

        documentGroup.MapPost("/query", async (
            [FromBody] DocumentQueryRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Querying documents");
            var result = await service.QueryAsync(request);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Query documents";
            operation.Description = "Searches for documents based on provided criteria with pagination support";
            return operation;
        });

        documentGroup.MapPost("/update", async (
            [FromBody] JsonElement documentElement,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Updating document");
            var result = await service.UpdateAsync(documentElement);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Update document";
            operation.Description = "Updates an existing document";
            return operation;
        });

        documentGroup.MapPost("/delete", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Deleting document with ID: {Id}", request.Id);
            var result = await service.DeleteAsync(request.Id);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Delete document";
            operation.Description = "Deletes a document by its unique identifier";
            return operation;
        });

        documentGroup.MapPost("/delete-many", async (
            [FromBody] DocumentIdsRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Deleting multiple documents");
            var result = await service.DeleteManyAsync(request.Ids);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Delete multiple documents";
            operation.Description = "Deletes multiple documents by their unique identifiers";
            return operation;
        });

        documentGroup.MapPost("/exists", async (
            [FromBody] DocumentIdRequest request,
            [FromServices] IDocumentService service) =>
        {
            _logger.LogInformation("Checking existence of document with ID: {Id}", request.Id);
            var result = await service.ExistsAsync(request.Id);
            return result.ToHttpResult();
        })
        .WithOpenApi(operation => {
            operation.Summary = "Check document existence";
            operation.Description = "Checks if a document exists by its unique identifier";
            return operation;
        });
    }
}
