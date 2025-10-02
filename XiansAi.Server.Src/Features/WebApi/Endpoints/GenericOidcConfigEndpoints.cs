using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Auth;

namespace Features.WebApi.Endpoints;

/// <summary>
/// API endpoints for Generic OIDC configuration management
/// Separate from User2Agent OIDC (/api/client/oidc-config/)
/// </summary>
public static class GenericOidcConfigEndpoints
{
    public static void MapGenericOidcConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/client/generic-oidc")
            .WithTags("WebAPI - Generic OIDC Config");

        // Get Generic OIDC configuration for current tenant
        group.MapGet("/", async (
            [FromServices] IGenericOidcConfigService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var result = await service.GetForTenantAsync(tenantId);
            return result.ToHttpResult();
        })
        .WithName("GetGenericOidcConfig")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Generic OIDC config for a tenant",
            Description = "Returns the decrypted Generic OIDC configuration JSON for the current tenant (from tenant context). Accessible to TenantAdmin and SysAdmin."
        })
        .RequiresValidTenantAdmin();

        // Create Generic OIDC configuration
        group.MapPost("/", async (
            [FromBody] object jsonConfig,
            [FromServices] IGenericOidcConfigService service,
            [FromServices] ITenantContext tenantContext,
            HttpContext ctx) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var actor = tenantContext.LoggedInUser ?? ctx.User?.Identity?.Name ?? "system";
            var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonConfig);
            var result = await service.UpsertAsync(tenantId, jsonString, actor);
            return result.ToHttpResult();
        })
        .WithName("CreateGenericOidcConfig")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create Generic OIDC config for a tenant",
            Description = "Creates the Generic OIDC configuration for the current tenant (from tenant context). Accessible to TenantAdmin and SysAdmin."
        })
        .RequiresValidTenantAdmin();

        // Update Generic OIDC configuration
        group.MapPut("/", async (
            [FromBody] object jsonConfig,
            [FromServices] IGenericOidcConfigService service,
            [FromServices] ITenantContext tenantContext,
            HttpContext ctx) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var actor = tenantContext.LoggedInUser ?? ctx.User?.Identity?.Name ?? "system";
            var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonConfig);
            var result = await service.UpsertAsync(tenantId, jsonString, actor);
            return result.ToHttpResult();
        })
        .WithName("UpdateGenericOidcConfig")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Generic OIDC config for a tenant",
            Description = "Updates the Generic OIDC configuration for the current tenant (from tenant context). Accessible to TenantAdmin and SysAdmin."
        })
        .RequiresValidTenantAdmin();

        // Delete Generic OIDC configuration
        group.MapDelete("/", async (
            [FromServices] IGenericOidcConfigService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var result = await service.DeleteAsync(tenantId);
            return result.ToHttpResult();
        })
        .WithName("DeleteGenericOidcConfig")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Generic OIDC config for a tenant",
            Description = "Deletes the Generic OIDC configuration for the current tenant (from tenant context). Accessible to TenantAdmin and SysAdmin."
        })
        .RequiresValidTenantAdmin();

        // SysAdmin list all Generic OIDC tenant configs
        group.MapGet("/admin", async (
            [FromServices] IGenericOidcConfigService service) =>
        {
            var result = await service.GetAllAsync();
            return result.ToHttpResult();
        })
        .WithName("AdminListAllGenericOidcConfigs")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List all Generic OIDC configs (SysAdmin)",
            Description = "Retrieves all tenants' Generic OIDC configuration entries (decrypted)."
        })
        .RequiresValidSysAdmin();
    }
}

