using System.Text.Json.Serialization;
using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Tenant-independent user management endpoints for the System Admin UI.
///
/// Authorization is declarative: each route names a <see cref="CapabilityActions"/> action that
/// <see cref="CapabilityMatrixFilter"/> resolves.
/// </summary>
public static class AdminGlobalUserEndpoints
{
    public sealed class PatchGlobalUserRequest
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }
    }

    public sealed class SetSysAdminRequest
    {
        [JsonPropertyName("isSysAdmin")]
        public required bool IsSysAdmin { get; init; }
    }

    public sealed class SetUserStatusRequest
    {
        [JsonPropertyName("enabled")]
        public required bool Enabled { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }

    public static void MapAdminGlobalUserEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/users")
            .WithTags("AdminAPI - Global User Management")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .EnforceCapabilities()
            .WithMetadata(TenantOptionalForSysAdminMetadata.Instance);

        // GET /api/v1/admin/users — list all users across tenants
        group.MapGet("", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromQuery] bool? isSysAdmin,
            [FromQuery] bool? isEnabled,
            [FromQuery] string? role,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var filter = new UserFilter
            {
                Page = page,
                PageSize = pageSize,
                Search = search,
                IsSysAdmin = isSysAdmin,
                IsEnabled = isEnabled,
                Role = role,
            };
            var result = await service.ListUsersAsync(filter);
            return result.ToHttpResult();
        })
        .WithName("AdminListGlobalUsers")
        .RequireCapability(CapabilityActions.GlobalUsersList);

        // GET /api/v1/admin/users/{userId} — single user with all tenant memberships
        group.MapGet("/{userId}", async (
            string userId,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var result = await service.GetUserWithMembershipsAsync(userId);
            return result.ToHttpResult();
        })
        .WithName("AdminGetGlobalUser")
        .RequireCapability(CapabilityActions.GlobalUsersGet);

        // PATCH /api/v1/admin/users/{userId} — update global profile (name, email)
        group.MapPatch("/{userId}", async (
            string userId,
            [FromBody] PatchGlobalUserRequest body,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var result = await service.UpdateProfileAsync(userId, body.Name, body.Email);
            return result.ToHttpResult();
        })
        .WithName("AdminPatchGlobalUser")
        .RequireCapability(CapabilityActions.GlobalUsersUpdate);

        // PUT /api/v1/admin/users/{userId}/sysadmin — grant or revoke SysAdmin flag
        group.MapPut("/{userId}/sysadmin", async (
            string userId,
            [FromBody] SetSysAdminRequest body,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var result = await service.SetSysAdminAsync(userId, body.IsSysAdmin);
            return result.ToHttpResult();
        })
        .WithName("AdminSetGlobalUserSysAdmin")
        .RequireCapability(CapabilityActions.GlobalUsersSysAdminSet);

        // PUT /api/v1/admin/users/{userId}/status — enable or disable user account
        group.MapPut("/{userId}/status", async (
            string userId,
            [FromBody] SetUserStatusRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var actingUserId = tenantContext.LoggedInUser ?? "system";
            var result = await service.SetStatusAsync(userId, body.Enabled, body.Reason, actingUserId);
            return result.ToHttpResult();
        })
        .WithName("AdminSetGlobalUserStatus")
        .RequireCapability(CapabilityActions.GlobalUsersStatusSet);

        // DELETE /api/v1/admin/users/{userId} — permanently delete the user account
        group.MapDelete("/{userId}", async (
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service) =>
        {
            var actingUserId = tenantContext.LoggedInUser ?? "system";
            var result = await service.DeleteUserAsync(userId, actingUserId);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .WithName("AdminDeleteGlobalUser")
        .RequireCapability(CapabilityActions.GlobalUsersDelete);
    }
}
