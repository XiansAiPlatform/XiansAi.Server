using System.Text.Json.Serialization;
using Features.AdminApi.Constants;
using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Providers.Auth;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi CRUD for tenant users that have <see cref="SystemRoles.TenantParticipant"/> or
/// <see cref="SystemRoles.TenantParticipantAdmin"/> on that tenant (they may also have TenantAdmin, TenantUser, or other roles).
/// Callers must use an API key whose owner is <see cref="SystemRoles.TenantAdmin"/> for the tenant or <see cref="SystemRoles.SysAdmin"/>.
/// Business logic lives in <see cref="ITenantParticipantUserService"/>; these endpoints only validate
/// the tenant route against the resolved context and delegate.
/// </summary>
public static class AdminUserEndpoints
{
    public sealed class CreateTenantParticipantUserRequest
    {
        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        /// <summary>Any tenant role: TenantAdmin, TenantUser, TenantParticipant, or TenantParticipantAdmin.</summary>
        [JsonPropertyName("role")]
        public required string Role { get; init; }
    }

    public sealed class UpdateTenantParticipantUserRequest
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("isApproved")]
        public bool? IsApproved { get; init; }
    }

    public static void MapAdminUserEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/users")
            .WithTags("AdminAPI - Tenant participant users")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        group.MapGet("", async (
            string tenantId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var result = await service.ListAsync(tenantId, page, pageSize, search);
            return result.ToHttpResult();
        })
        .WithName("AdminListTenantParticipantUsers");

        group.MapGet("/{userId}", async (
            string tenantId,
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var result = await service.GetAsync(tenantId, userId);
            return result.ToHttpResult();
        })
        .WithName("AdminGetTenantParticipantUser");

        group.MapPost("", async (
            string tenantId,
            [FromBody] CreateTenantParticipantUserRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var result = await service.CreateAsync(tenantId, body.Email, body.Name, body.Role);
            if (result.IsSuccess && result.Data != null)
            {
                var location = AdminApiConstants.BuildVersionedPath(
                    $"tenants/{Uri.EscapeDataString(tenantId)}/users/{Uri.EscapeDataString(result.Data.UserId)}");
                return Results.Created(location, result.Data);
            }
            return result.ToHttpResult();
        })
        .WithName("AdminCreateTenantParticipantUser");

        group.MapPatch("/{userId}", async (
            string tenantId,
            string userId,
            [FromBody] UpdateTenantParticipantUserRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var isSysAdmin = tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) == true;
            var result = await service.UpdateAsync(tenantId, userId, body.Name, body.Email, body.Role, body.IsApproved, isSysAdmin);
            return result.ToHttpResult();
        })
        .WithName("AdminUpdateTenantParticipantUser");

        group.MapDelete("/{userId}", async (
            string tenantId,
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var isSysAdmin = tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) == true;
            var result = await service.DeleteAsync(tenantId, userId, isSysAdmin);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .WithName("AdminDeleteTenantParticipantUser");

        // DELETE /api/v1/admin/tenants/{tenantId}/users/{userId}/roles/{role}
        // Removes a single role from a user's tenant membership without affecting other roles.
        // If the user has no remaining roles the tenant membership is removed entirely.
        group.MapDelete("/{userId}/roles/{role}", async (
            string tenantId,
            string userId,
            string role,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantParticipantUserService service,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            if (!TenantRouteMatchesContext(tenantContext, tenantId, loggerFactory, out var forbid))
                return forbid;

            var isSysAdmin = tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) == true;
            var result = await service.RemoveRoleAsync(tenantId, userId, role, isSysAdmin);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .WithName("AdminRemoveTenantUserRole");
    }

    /// <summary>
    /// Ensures the tenant in the route matches the resolved tenant context.
    /// Returns false with a 403 result via <paramref name="forbid"/> on mismatch.
    /// </summary>
    private static bool TenantRouteMatchesContext(
        ITenantContext ctx,
        string tenantIdFromRoute,
        ILoggerFactory loggerFactory,
        out IResult forbid)
    {
        if (!string.IsNullOrEmpty(tenantIdFromRoute) && !string.IsNullOrEmpty(ctx.TenantId) &&
            string.Equals(tenantIdFromRoute, ctx.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            forbid = Results.Empty;
            return true;
        }

        loggerFactory.CreateLogger("AdminUserEndpoints").LogWarning(
            "Tenant route {RouteTenant} does not match resolved tenant context {CtxTenant}",
            LogSanitizer.Sanitize(tenantIdFromRoute), LogSanitizer.Sanitize(ctx.TenantId));
        forbid = Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);
        return false;
    }
}
