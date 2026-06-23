using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Tenant-independent user management endpoints for the System Admin UI.
/// All endpoints require the caller to be a System Admin (enforced here);
/// business logic lives in <see cref="IGlobalUserAdminService"/>.
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
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // GET /api/v1/admin/users — list all users across tenants
        group.MapGet("", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromQuery] bool? isSysAdmin,
            [FromQuery] bool? isEnabled,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!IsSysAdminCaller(tenantContext, loggerFactory, "list global users", out var forbid))
                return forbid;

            var filter = new UserFilter
            {
                Page = page,
                PageSize = pageSize,
                Search = search,
                IsSysAdmin = isSysAdmin,
                IsEnabled = isEnabled,
            };
            var result = await service.ListUsersAsync(filter);
            return result.ToHttpResult();
        })
        .WithName("AdminListGlobalUsers");

        // GET /api/v1/admin/users/{userId} — single user with all tenant memberships
        group.MapGet("/{userId}", async (
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!IsSysAdminCaller(tenantContext, loggerFactory, "get global user", out var forbid))
                return forbid;

            var result = await service.GetUserWithMembershipsAsync(userId);
            return result.ToHttpResult();
        })
        .WithName("AdminGetGlobalUser");

        // PATCH /api/v1/admin/users/{userId} — update global profile (name, email)
        group.MapPatch("/{userId}", async (
            string userId,
            [FromBody] PatchGlobalUserRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!IsSysAdminCaller(tenantContext, loggerFactory, "patch global user", out var forbid))
                return forbid;

            var result = await service.UpdateProfileAsync(userId, body.Name, body.Email);
            return result.ToHttpResult();
        })
        .WithName("AdminPatchGlobalUser");

        // PUT /api/v1/admin/users/{userId}/sysadmin — grant or revoke SysAdmin flag
        group.MapPut("/{userId}/sysadmin", async (
            string userId,
            [FromBody] SetSysAdminRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!IsSysAdminCaller(tenantContext, loggerFactory, "set sysadmin flag", out var forbid))
                return forbid;

            var result = await service.SetSysAdminAsync(userId, body.IsSysAdmin);
            return result.ToHttpResult();
        })
        .WithName("AdminSetGlobalUserSysAdmin");

        // PUT /api/v1/admin/users/{userId}/status — enable or disable user account
        group.MapPut("/{userId}/status", async (
            string userId,
            [FromBody] SetUserStatusRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IGlobalUserAdminService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!IsSysAdminCaller(tenantContext, loggerFactory, "set user status", out var forbid))
                return forbid;

            var actingUserId = tenantContext.LoggedInUser ?? "system";
            var result = await service.SetStatusAsync(userId, body.Enabled, body.Reason, actingUserId);
            return result.ToHttpResult();
        })
        .WithName("AdminSetGlobalUserStatus");
    }

    /// <summary>
    /// Ensures the caller has the SysAdmin role. Returns false and a 403 result via
    /// <paramref name="forbid"/> when access should be denied.
    /// </summary>
    private static bool IsSysAdminCaller(
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory,
        string operation,
        out IResult forbid)
    {
        if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) == true)
        {
            forbid = Results.Empty;
            return true;
        }

        loggerFactory.CreateLogger("AdminGlobalUserEndpoints").LogWarning(
            "Access denied: {Operation} requires SysAdmin role. User: {UserId}",
            operation, LogSanitizer.Sanitize(tenantContext.LoggedInUser));
        forbid = Results.Json(
            new { message = "Access denied: Only system administrators can manage users" },
            statusCode: StatusCodes.Status403Forbidden);
        return false;
    }
}
