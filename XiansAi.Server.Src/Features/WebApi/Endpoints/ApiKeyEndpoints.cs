using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Shared.Services;
using Shared.Auth;
using Microsoft.AspNetCore.Http;
using Features.WebApi.Auth;

namespace XiansAi.Server.Features.WebApi.Endpoints
{
    public static class ApiKeyEndpoints
    {
        public static void MapApiKeyEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/client/apikeys")
                .WithTags("WebAPI - API Keys")
                .RequiresValidTenant();

            // Create API key
            group.MapPost("/create", async (
                [FromServices] IApiKeyService apiKeyService,
                [FromServices] ITenantContext tenantContext,
                [FromBody] CreateApiKeyRequest request,
                HttpContext httpContext) =>
            {
                var userId = tenantContext.LoggedInUser ?? "system";
                try
                {
                    var (apiKey, meta) = await apiKeyService.CreateApiKeyAsync(tenantContext.TenantId, request.Name, userId);
                    // Return the raw key only once
                    return Results.Ok(new { apiKey, meta.Id, meta.Name, meta.CreatedAt, meta.CreatedBy });
                }
                catch (DuplicateApiKeyNameException ex)
                {
                    return Results.Problem(ex.Message, statusCode: 409, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem("An unexpected error occurred while creating the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message
                    });
                }
            })
            .WithName("CreateApiKey")
            .WithOpenApi(op => {
                op.Summary = "Create a new API key";
                op.Description = "Creates a new API key for the current tenant. The raw key is returned only once.";
                return op;
            });

            // List API keys
            group.MapGet("", async (
                [FromServices] IApiKeyService apiKeyService,
                [FromServices] ITenantContext tenantContext) =>
            {
                try
                {
                    var keys = await apiKeyService.GetApiKeysAsync(tenantContext.TenantId);
                    // Never return the raw key or hash
                    var result = keys.Select(k => new {
                        k.Id, k.Name, k.CreatedAt, k.CreatedBy, k.LastRotatedAt
                    });
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return Results.Problem("An unexpected error occurred while retrieving API keys.", statusCode: 500, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message
                    });
                }
            })
            .WithName("ListApiKeys")
            .WithOpenApi(op => {
                op.Summary = "List all API keys for the tenant";
                op.Description = "Returns metadata for all API keys for the current tenant.";
                return op;
            });

            // Revoke API key
            group.MapPost("{id}/revoke", async (
                [FromRoute] string id,
                [FromServices] IApiKeyService apiKeyService,
                [FromServices] ITenantContext tenantContext) =>
            {
                try
                {
                    var ok = await apiKeyService.RevokeApiKeyAsync(id, tenantContext.TenantId);
                    return ok ? Results.Ok() : Results.NotFound();
                }
                catch (Exception ex)
                {
                    return Results.Problem("An unexpected error occurred while revoking the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message
                    });
                }
            })
            .WithName("RevokeApiKey")
            .WithOpenApi(op => {
                op.Summary = "Revoke an API key";
                op.Description = "Revokes the specified API key for the current tenant.";
                return op;
            });

            // Rotate API key
            group.MapPost("{id}/rotate", async (
                [FromRoute] string id,
                [FromServices] IApiKeyService apiKeyService,
                [FromServices] ITenantContext tenantContext) =>
            {
                try
                {
                    var rotated = await apiKeyService.RotateApiKeyAsync(id, tenantContext.TenantId);
                    if (rotated == null) return Results.NotFound();
                    var (apiKey, meta) = rotated.Value;
                    return Results.Ok(new { apiKey, meta.Id, meta.Name, meta.CreatedAt, meta.CreatedBy, meta.LastRotatedAt });
                }
                catch (Exception ex)
                {
                    return Results.Problem("An unexpected error occurred while rotating the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = ex.Message
                    });
                }
            })
            .WithName("RotateApiKey")
            .WithOpenApi(op => {
                op.Summary = "Rotate an API key";
                op.Description = "Rotates the specified API key and returns the new raw key (only once).";
                return op;
            });
        }
    }

    public class CreateApiKeyRequest
    {
        public string Name { get; set; } = string.Empty;
    }
}
