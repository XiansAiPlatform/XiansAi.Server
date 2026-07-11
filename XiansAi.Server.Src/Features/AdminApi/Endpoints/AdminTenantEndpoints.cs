using Shared.Auth;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for tenant management.
/// These are administrative operations for managing tenants.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminTenantEndpoints
{
    /// <summary>
    /// Maps all AdminApi tenant endpoints.
    /// </summary>
    public static void MapAdminTenantEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminTenantGroup = adminApiGroup.MapGroup("/tenants")
            .WithTags("AdminAPI - Tenant Management")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List All Tenants - SysAdmin only (prevents TenantAdmin from enumerating all tenants)
        adminTenantGroup.MapGet("", async (
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: List tenants requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can list all tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var result = await tenantService.GetAllTenants();
            return result.ToHttpResult();
        })
        .WithName("ListTenants")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get Tenant by TenantId - SysAdmin only (TenantAdmin should use tenant-scoped endpoints)
        adminTenantGroup.MapGet("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Get tenant by ID requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can retrieve tenant details by ID" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            // Honor the standard "Cache-Control: no-cache" request header: when present, read the
            // tenant directly from the database (bypassing, then refreshing, the tenant cache).
            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted, bypassCache);
            return result.ToHttpResult();
        })
        .WithName("GetTenantByTenantId")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get Tenant Metadata - SysAdmin only. This is the only endpoint that returns
        // metadata with Secret values decrypted; tenant payloads elsewhere carry the
        // stored (encrypted) form.
        adminTenantGroup.MapGet("/{tenantId}/metadata", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Get tenant metadata requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can retrieve tenant metadata" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantMetadata(tenantId, httpContext.RequestAborted, bypassCache);
            return result.ToHttpResult();
        })
        .WithName("GetTenantMetadata")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only.
        adminTenantGroup.MapGet("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Get tenant metadata requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can retrieve tenant metadata" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantMetadataByKey(tenantId, key, httpContext.RequestAborted, bypassCache);
            return result.ToHttpResult();
        })
        .WithName("GetTenantMetadataByKey")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Upsert a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only.
        adminTenantGroup.MapPut("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            [FromBody] UpsertTenantMetadataRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Upsert tenant metadata requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can modify tenant metadata" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await tenantService.UpsertTenantMetadata(tenantId, key, request);
            return result.ToHttpResult();
        })
        .WithName("UpsertTenantMetadata")
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Delete a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only.
        adminTenantGroup.MapDelete("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Delete tenant metadata requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can modify tenant metadata" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var result = await tenantService.DeleteTenantMetadata(tenantId, key);
            return result.ToHttpResult();
        })
        .WithName("DeleteTenantMetadata")
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Create Tenant - No X-Tenant-Id header required (creating new tenant)
        adminTenantGroup.MapPost("", async (
            [FromBody] CreateTenantRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Create tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can create tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var createdBy = tenantContext.LoggedInUser ?? "system";
            var result = await tenantService.CreateTenant(request, createdBy);
            return result.ToHttpResult();
        })
        .WithName("CreateTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Update Tenant - SysAdmin only
        adminTenantGroup.MapPatch("/{tenantId}", async (
            string tenantId,
            [FromBody] UpdateTenantRequest request,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Update tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can update tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the update operation
            var result = await tenantService.UpdateTenant(tenantResult.Data.Id, request);
            return result.ToHttpResult();
        })
        .WithName("UpdateTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Delete Tenant - SysAdmin only
        adminTenantGroup.MapDelete("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Delete tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can delete tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the delete operation
            var result = await tenantService.DeleteTenant(tenantResult.Data.Id);
            return result.ToHttpResult();
        })
        .WithName("DeleteTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;
    }
}


