using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Auth;

namespace Features.WebApi.Endpoints;

public static class OidcConfigEndpoints
{
    public static void MapOidcConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/client/oidc-config")
            .WithTags("WebAPI - OIDC Config");

        // Unified tenant-scoped endpoints (TenantAdmin and SysAdmin), tenant from tenant context
        group.MapPost("/", async (
            [FromBody] object jsonConfig,
            [FromServices] ITenantOidcConfigService service,
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
        .WithName("UpsertOidcConfigCreate")
        
        .RequiresValidTenantAdmin();

        group.MapPut("/", async (
            [FromBody] object jsonConfig,
            [FromServices] ITenantOidcConfigService service,
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
        .WithName("UpsertOidcConfigUpdate")
        
        .RequiresValidTenantAdmin();

        group.MapDelete("/", async (
            [FromServices] ITenantOidcConfigService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var result = await service.DeleteAsync(tenantId);
            return result.ToHttpResult();
        })
        .WithName("DeleteOidcConfig")
        
        .RequiresValidTenantAdmin();

        // SysAdmin list all tenant configs
        group.MapGet("/admin", async (
            [FromServices] ITenantOidcConfigService service) =>
        {
            var result = await service.GetAllAsync();
            return result.ToHttpResult();
        })
        .WithName("AdminListAllOidcConfigs")
        
        .RequiresValidSysAdmin();
        
        group.MapGet("/", async (
            [FromServices] ITenantOidcConfigService service,
            [FromServices] ITenantContext tenantContext) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId)) return Results.BadRequest("Tenant is not resolved");
            var result = await service.GetForTenantAsync(tenantId);
            return result.ToHttpResult();
        })
        .WithName("GetOidcConfig")
        
        .RequiresValidTenantAdmin();
    }
}


