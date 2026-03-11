using Shared.Auth;
using Shared.Services;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List All Tenants",
            Description = "Retrieve all tenants in the system. SysAdmin only. X-Tenant-Id header is NOT required for this endpoint."
        });

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
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            return result.ToHttpResult();
        })
        .WithName("GetTenantByTenantId")
        .Produces(StatusCodes.Status403Forbidden)
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Tenant by TenantId",
            Description = "Retrieve a specific tenant by its tenantId (e.g., 'spacex01'). SysAdmin only. X-Tenant-Id header is NOT required."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create Tenant",
            Description = "Create a new tenant. SysAdmin only. X-Tenant-Id header is NOT required."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Tenant",
            Description = "Update an existing tenant by tenantId (e.g., 'spacex01'). SysAdmin only. X-Tenant-Id header is NOT required."
        });

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Tenant",
            Description = "Delete a tenant by tenantId (e.g., 'spacex01'). SysAdmin only. X-Tenant-Id header is NOT required."
        });
    }
}


