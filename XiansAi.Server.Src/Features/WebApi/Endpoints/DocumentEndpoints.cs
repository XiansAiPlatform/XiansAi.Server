using Microsoft.AspNetCore.Mvc;
using Features.WebApi.Auth;
using Features.WebApi.Services;
using Shared.Utils.Services;
using Features.Shared.Configuration;

namespace Features.WebApi.Endpoints;

/// <summary>
/// Provides endpoints for managing documents through the Web UI.
/// Allows users to explore, edit, and delete agent documents.
/// </summary>
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder routes)
    {
        var documentGroup = routes.MapGroup("/api/client/documents")
            .WithTags("WebAPI - Documents")
            .RequiresValidTenant()
            .RequireAuthorization()
            .WithGlobalRateLimit();

        // Get all document types for a specific agent
        documentGroup.MapGet("/agents/{agentId}/types", async (
            string agentId,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentTypesByAgentAsync(agentId);
            return result.ToHttpResult();
        })
        .WithName("Get Document Types by Agent")
        .WithOpenApi(operation => {
            operation.Summary = "Get document types for an agent";
            operation.Description = "Retrieves all distinct document types for a specific agent";
            return operation;
        });

        // Get documents by agent and type with pagination
        documentGroup.MapGet("/agents/{agentId}/types/{type}", async (
            string agentId,
            string type,
            [FromQuery] int skip,
            [FromQuery] int limit,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentsByAgentAndTypeAsync(agentId, type, skip, limit);
            return result.ToHttpResult();
        })
        .WithName("Get Documents by Agent and Type")
        .WithOpenApi(operation => {
            operation.Summary = "Get documents by agent and type";
            operation.Description = "Retrieves documents for a specific agent and document type with pagination support";
            return operation;
        });

        // Get a single document by ID
        documentGroup.MapGet("/{id}", async (
            string id,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentByIdAsync(id);
            return result.ToHttpResult();
        })
        .WithName("Get Document by ID")
        .WithOpenApi(operation => {
            operation.Summary = "Get document by ID";
            operation.Description = "Retrieves a single document by its unique identifier";
            return operation;
        });

        // Update a document
        documentGroup.MapPut("/{id}", async (
            string id,
            [FromBody] DocumentUpdateRequest request,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.UpdateDocumentAsync(id, request);
            return result.ToHttpResult();
        })
        .WithName("Update Document")
        .WithOpenApi(operation => {
            operation.Summary = "Update a document";
            operation.Description = "Updates an existing document with new data";
            return operation;
        });

        // Delete a single document
        documentGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.DeleteDocumentAsync(id);
            return result.ToHttpResult();
        })
        .WithName("Delete Document")
        .WithOpenApi(operation => {
            operation.Summary = "Delete a document";
            operation.Description = "Deletes a document by its unique identifier";
            return operation;
        });

        // Delete multiple documents
        documentGroup.MapPost("/bulk-delete", async (
            [FromBody] List<string> ids,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.DeleteDocumentsAsync(ids);
            return result.ToHttpResult();
        })
        .WithName("Delete Multiple Documents")
        .WithOpenApi(operation => {
            operation.Summary = "Delete multiple documents";
            operation.Description = "Deletes multiple documents by their unique identifiers";
            return operation;
        });
    }
}
