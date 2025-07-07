using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class RoleManagementEndpoints
{
    public static void MapRoleManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/roles")
            .WithTags("WebAPI - Role Management")
            .RequiresValidTenant();

        group.MapGet("/user/{userId}/tenant/{tenantId}", async (
            string userId,
            string tenantId,
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.GetUserRolesAsync(userId, tenantId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin))
        .WithName("GetUserRoles")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get roles for a specific user in a tenant",
            Description = "Retrieves all roles assigned to a user within a specific tenant"
        });

        group.MapGet("/tenant/{tenantId}/role/{role}", async (
            string tenantId,
            string role,
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.GetUsersByRoleAsync(role, tenantId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin))
        .WithName("GetUsersByRole")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get all users with a specific role",
            Description = "Retrieves all users that have been assigned a specific role within a tenant"
        });

        group.MapGet("/tenant/{tenantId}/admins", async (
            string tenantId,
            [FromServices] IRoleManagementService roleService) =>
        {
            // Get all users with the TenantAdmin role for the given tenant
            var result = await roleService.GetUsersInfoByRoleAsync(SystemRoles.TenantAdmin, tenantId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin))
        .WithName("GetAllTenantAdmins")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get all tenant admins by tenant ID",
            Description = "Retrieves all users with the TenantAdmin role for the specified tenant"
        });

        group.MapGet("/current", async (
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.GetCurrentUserRolesAsync();
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetCurrentUserRoles")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get current user's roles",
            Description = "Retrieves the roles for the currently authenticated user in the current tenant context"
        });

        group.MapPost("/promote-tenant-admin", async (
            [FromBody] RolesDto request,
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.PromoteToTenantAdminAsync(request.UserId, request.TenantId);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin))
        .WithName("PromoteToTenantAdmin")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Promote user to tenant admin",
            Description = "Promotes a user to tenant administrator role. Requires System Admin or Tenant Admin privileges."
        });

        group.MapPost("/assign", async (
            [FromBody] RolesDto request,
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.AssignRolesToUserAsync(
                request.UserId,
                request.TenantId,
                request.Roles);
            return Results.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin))
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Assign a user to a role",
            Description = "Assign a user to system roles. Requires System Admin privileges."
        });

        group.MapPost("/bootstrap-admin", async (
            [FromBody] BootstrapAdminDto request,
            [FromServices] IRoleManagementService roleService) =>
        {
            var existingAdmins = await roleService.GetUsersByRoleAsync(SystemRoles.SysAdmin, request.TenantId);
            if (existingAdmins.Data?.Any() == true)
            {
                return Results.BadRequest("System admin already exists. Use regular role assignment.");
            }

            var result = await roleService.AssignRolesToUserAsync(
                request.UserId,
                request.TenantId,
                new List<string> { SystemRoles.SysAdmin });

            return Results.Ok(result);
        });

        group.MapDelete("/remove", async (
            [FromBody] RolesDto request,
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.RemoveRoleFromUserAsync(
                request.UserId,
                request.TenantId,
                request.Roles);
            return Results.Ok(result);
        })
        .RequireAuthorization(policy => policy.RequireRole(SystemRoles.SysAdmin))
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Remove a user from a role",
            Description = "Remove a user from a system roles. Requires System Admin privileges."
        });
    }
}

public class BootstrapAdminDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;
}