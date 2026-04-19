using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using Features.WebApi.Services;

namespace Features.WebApi.Endpoints;

public static class RoleManagementEndpoints
{
    public static void MapRoleManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/roles")
            .WithTags("WebAPI - Role Management")
            .RequiresValidTenant()
            .RequireAuthorization();


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
        .RequiresValidTenantAdmin()
        .WithName("GetAllTenantAdmins")
        ;

        group.MapGet("/current", async (
            [FromServices] IRoleManagementService roleService) =>
        {
            var result = await roleService.GetCurrentUserRolesAsync();
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .WithName("GetCurrentUserRoles")
        ;

        group.MapPost("/tenant/{tenantId}/admins", async (
            string tenantId,
            [FromBody] RoleDto dto,
            [FromServices] IRoleManagementService roleService) =>
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return Results.BadRequest("TenantId is required.");
            }

            dto.Role = SystemRoles.TenantAdmin;
            dto.TenantId = tenantId;

            var result = await roleService.AssignTenantRoletoUserAsync(dto);
            return result.IsSuccess
                ? Results.Ok(result.Data)
                : Results.Problem(result.ErrorMessage, statusCode: (int)result.StatusCode);
        })
        .RequiresValidTenantAdmin()
        .WithName("AssignTenantAdmin")
        ;

        group.MapDelete("/tenant/{tenantId}/admins/{userId}", async (
            string tenantId,
            string userId,
            [FromServices] IRoleManagementService roleService) =>
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                return Results.BadRequest("TenantId is required.");
            }
            if (string.IsNullOrEmpty(userId))
            {
                return Results.BadRequest("UserId is required.");
            }
            var request = new RoleDto
            {
                UserId = userId,
                TenantId = tenantId,
                Role = SystemRoles.TenantAdmin
            };

            ServiceResult<bool> result = await roleService.RemoveRoleFromUserAsync(request);

            return Results.Ok(result);
        })
        .RequiresValidTenantAdmin()
        ;
    }
}

public class BootstrapAdminDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;
}