using Features.AdminApi.Auth;
using Features.AdminApi.Utils;
using Features.WebApi.Auth;
using Shared.Services;
using Shared.Auth;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using Shared.Repositories;

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
            .WithTags("AdminAPI - Tenant Management");

        // List All Tenants - No X-Tenant-Id header required (you need to list tenants first!)
        adminTenantGroup.MapGet("", async (
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminApiAuthUtils");
            // Check if user is SysAdmin (with fallback lookup support)
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(tenantContext, userRepository, httpContext, logger);
            if (!isSysAdmin)
            {
                return errorResult!;
            }
            
            var result = await tenantService.GetAllTenants();
            return result.ToHttpResult();
        })
        .RequiresToken() // Only require token, not tenant header (role check done in endpoint)
        .WithName("ListTenants")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "List All Tenants",
            Description = "Retrieve all tenants in the system. Requires admin permissions. X-Tenant-Id header is NOT required for this endpoint."
        });

        // Get Tenant by TenantId - No X-Tenant-Id header required (tenant ID is in path)
        adminTenantGroup.MapGet("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository) =>
        {
            // Check if user is SysAdmin
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(tenantContext, userRepository, httpContext, null);
            if (!isSysAdmin)
            {
                return errorResult!;
            }
            
            var result = await tenantService.GetTenantByTenantId(tenantId);
            return result.ToHttpResult();
        })
        .RequiresToken() // Role check done in endpoint
        .WithName("GetTenantByTenantId")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Tenant by TenantId",
            Description = "Retrieve a specific tenant by its tenantId (e.g., 'spacex01'). Requires admin permissions. X-Tenant-Id header is NOT required."
        });

        // Create Tenant - No X-Tenant-Id header required (creating new tenant)
        adminTenantGroup.MapPost("", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository) =>
        {
            // Only SysAdmin can create tenants
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(tenantContext, userRepository, httpContext, null);
            if (!isSysAdmin)
            {
                return errorResult!;
            }

            var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            var createdBy = userIdClaim ?? tenantContext.LoggedInUser ?? "system";
            
            var result = await tenantService.CreateTenant(request, createdBy);
            return result.ToHttpResult();
        })
        .RequiresToken() // Role check done in endpoint (SysAdmin only)
        .WithName("CreateTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Create Tenant",
            Description = "Create a new tenant. Requires system admin permissions. X-Tenant-Id header is NOT required."
        });

        // Update Tenant - No X-Tenant-Id header required (tenant ID is in path)
        adminTenantGroup.MapPatch("/{tenantId}", async (
            string tenantId,
            [FromBody] UpdateTenantRequest request,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository) =>
        {
            // Only SysAdmin can update tenants
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(tenantContext, userRepository, httpContext, null);
            if (!isSysAdmin)
            {
                return errorResult!;
            }
            
            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the update operation
            var result = await tenantService.UpdateTenant(tenantResult.Data.Id, request);
            return result.ToHttpResult();
        })
        .RequiresToken() // Role check done in endpoint (SysAdmin only)
        .WithName("UpdateTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Update Tenant",
            Description = "Update an existing tenant by tenantId (e.g., 'spacex01'). Requires system admin permissions. X-Tenant-Id header is NOT required."
        });

        // Delete Tenant - No X-Tenant-Id header required (tenant ID is in path)
        adminTenantGroup.MapDelete("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository) =>
        {
            // Only SysAdmin can delete tenants
            var (isSysAdmin, errorResult) = await AdminApiAuthUtils.CheckSysAdminAsync(tenantContext, userRepository, httpContext, null);
            if (!isSysAdmin)
            {
                return errorResult!;
            }

            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the delete operation
            var result = await tenantService.DeleteTenant(tenantResult.Data.Id);
            return result.ToHttpResult();
        })
        .RequiresToken() // Role check done in endpoint (SysAdmin only)
        .WithName("DeleteTenant")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Delete Tenant",
            Description = "Delete a tenant by tenantId (e.g., 'spacex01'). Requires system admin permissions. X-Tenant-Id header is NOT required."
        });
    }
}

