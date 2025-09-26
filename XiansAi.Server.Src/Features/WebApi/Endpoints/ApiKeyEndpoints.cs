using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Auth;
using Features.WebApi.Auth;
using Shared.Utils.Services; 

namespace Features.WebApi.Endpoints
{
    public static class ApiKeyEndpoints
    {
        public static void MapApiKeyEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/client/apikeys")
                .WithTags("WebAPI - API Keys")
                .RequiresValidTenant()
                .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin));

            // Create API key
            group.MapPost("/create", async (
                [FromServices] IApiKeyService apiKeyService,
                [FromServices] ITenantContext tenantContext,
                [FromBody] CreateApiKeyRequest request,
                HttpContext httpContext) =>
            {
                var userId = tenantContext.LoggedInUser ?? "system";
                var result = await apiKeyService.CreateApiKeyAsync(tenantContext.TenantId, request.Name, userId);
                if (result.IsSuccess)
                {
                    var (apiKey, meta) = result.Data;
                    return Results.Ok(new { apiKey, meta.Id, meta.Name, meta.CreatedAt, meta.CreatedBy });
                }
                if (result.StatusCode == StatusCode.Conflict)
                {
                    return Results.Problem(result.ErrorMessage, statusCode: 409, extensions: new Dictionary<string, object?>
                    {
                        ["error"] = result.ErrorMessage
                    });
                }
                return Results.Problem(result.ErrorMessage ?? "An unexpected error occurred while creating the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                {
                    ["error"] = result.ErrorMessage
                });
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
                var result = await apiKeyService.GetApiKeysAsync(tenantContext.TenantId);
                if (result.IsSuccess)
                {
                    var keys = result.Data;
                    if (keys == null || keys.Count == 0)
                    {
                        return Results.Ok(new List<object>());
                    }
                    var response = keys.Select(k => new {
                        k.Id, k.Name, k.CreatedAt, k.CreatedBy, k.LastRotatedAt
                    });
                    return Results.Ok(response);
                }
                return Results.Problem(result.ErrorMessage ?? "An unexpected error occurred while retrieving API keys.", statusCode: 500, extensions: new Dictionary<string, object?>
                {
                    ["error"] = result.ErrorMessage
                });
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
                var result = await apiKeyService.RevokeApiKeyAsync(id, tenantContext.TenantId);
                if (result.IsSuccess && result.Data)
                {
                    return Results.Ok();
                }
                if (result.StatusCode == StatusCode.NotFound)
                {
                    return Results.NotFound();
                }
                return Results.Problem(result.ErrorMessage ?? "An unexpected error occurred while revoking the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                {
                    ["error"] = result.ErrorMessage
                });
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
                var result = await apiKeyService.RotateApiKeyAsync(id, tenantContext.TenantId);
                if (result.IsSuccess && result.Data != null)
                {
                    var (apiKey, meta) = result.Data.Value;
                    return Results.Ok(new { apiKey, meta.Id, meta.Name, meta.CreatedAt, meta.CreatedBy, meta.LastRotatedAt });
                }
                if (result.StatusCode == StatusCode.NotFound)
                {
                    return Results.NotFound();
                }
                return Results.Problem(result.ErrorMessage ?? "An unexpected error occurred while rotating the API key.", statusCode: 500, extensions: new Dictionary<string, object?>
                {
                    ["error"] = result.ErrorMessage
                });
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
