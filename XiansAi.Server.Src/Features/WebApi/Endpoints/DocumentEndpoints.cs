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

        // Get all document types and activation names for a specific agent
        documentGroup.MapGet("/agents/{agentId}/types", async (
            string agentId,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentTypesAndActivationsByAgentAsync(agentId);
            return result.ToHttpResult();
        })
        .WithName("Get Document Types and Activations by Agent")
        
        .WithSummary("Get document types and activation names for an agent")
        .WithDescription("Retrieves all distinct document types and activation names for a specific agent");

        // Get documents by agent and type with pagination
        documentGroup.MapGet("/agents/{agentId}/types/{type}", async (
            string agentId,
            string type,
            [FromQuery] int skip,
            [FromQuery] int limit,
            [FromQuery] string? activationName,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentsByAgentAndTypeAsync(agentId, type, skip, limit, activationName);
            return result.ToHttpResult();
        })
        .WithName("Get Documents by Agent and Type")
        
        .WithSummary("Get documents by agent and type")
        .WithDescription("Retrieves documents for a specific agent and document type with pagination support. Optionally filter by activation name.");

        // Get a single document by ID
        documentGroup.MapGet("/{id}", async (
            string id,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.GetDocumentByIdAsync(id);
            return result.ToHttpResult();
        })
        .WithName("Get Document by ID")
        
        .WithSummary("Get document by ID")
        .WithDescription("Retrieves a single document by its unique identifier");

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
        
        .WithSummary("Update a document")
        .WithDescription("Updates an existing document with new data");

        // Delete a single document
        documentGroup.MapDelete("/{id}", async (
            string id,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.DeleteDocumentAsync(id);
            return result.ToHttpResult();
        })
        .WithName("Delete Document")
        
        .WithSummary("Delete a document")
        .WithDescription("Deletes a document by its unique identifier");

        // Delete multiple documents
        documentGroup.MapPost("/bulk-delete", async (
            [FromBody] List<string> ids,
            [FromServices] IDocumentService service) =>
        {
            var result = await service.DeleteDocumentsAsync(ids);
            return result.ToHttpResult();
        })
        .WithName("Delete Multiple Documents")
        
        .WithSummary("Delete multiple documents")
        .WithDescription("Deletes multiple documents by their unique identifiers");
    }
}
