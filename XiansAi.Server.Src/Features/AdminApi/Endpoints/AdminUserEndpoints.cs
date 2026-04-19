using System.Text.Json.Serialization;
using Features.AdminApi.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi CRUD for tenant users that have <see cref="SystemRoles.TenantParticipant"/> or
/// <see cref="SystemRoles.TenantParticipantAdmin"/> on that tenant (they may also have TenantAdmin, TenantUser, or other roles).
/// Callers must use an API key whose owner is <see cref="SystemRoles.TenantAdmin"/> for the tenant or <see cref="SystemRoles.SysAdmin"/>.
/// </summary>
public static class AdminUserEndpoints
{
    public sealed class TenantParticipantUserResponse
    {
        [JsonPropertyName("userId")]
        public required string UserId { get; init; }

        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        /// <summary>Preferred participant role when both are present.</summary>
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        /// <summary>True only when the tenant role is approved and the user is not locked out.</summary>
        [JsonPropertyName("isApproved")]
        public required bool IsApproved { get; init; }
    }

    public sealed class CreateTenantParticipantUserRequest
    {
        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        /// <summary>Either <see cref="SystemRoles.TenantParticipant"/> or <see cref="SystemRoles.TenantParticipantAdmin"/>.</summary>
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
            .RequireAuthorization("AdminEndpointAuthPolicy");

        group.MapGet("", async (
            string tenantId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromQuery] string? search,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserEndpoints");
            if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
                return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

            var filter = new UserFilter
            {
                Page = page > 0 ? page : 1,
                PageSize = pageSize > 0 ? pageSize : 20,
                Type = UserTypeFilter.PARTICIPANT_SCOPE,
                Tenant = tenantId,
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()
            };

            try
            {
                var paged = await userRepository.GetAllUsersByTenantAsync(filter);
                var items = paged.Users
                    .Select(u => MapToResponse(u, tenantId))
                    .Where(r => r != null)
                    .Cast<TenantParticipantUserResponse>()
                    .ToList();

                return Results.Ok(new
                {
                    users = items,
                    totalCount = paged.TotalCount,
                    page = filter.Page,
                    pageSize = filter.PageSize
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error listing participant users for tenant {TenantId}", tenantId);
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("AdminListTenantParticipantUsers")
        ;

        group.MapGet("/{userId}", async (
            string tenantId,
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserEndpoints");
            if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
                return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

            var user = await userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return Results.NotFound(new { message = "User not found" });

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tr == null || !HasParticipantRole(tr))
            {
                logger.LogWarning(
                    "User {UserId} does not have a participant role in tenant {TenantId}",
                    userId, tenantId);
                return Results.NotFound(new { message = "User not found in this tenant as a participant user" });
            }

            var response = MapToResponse(user, tenantId);
            return response == null
                ? Results.NotFound()
                : Results.Ok(response);
        })
        .WithName("AdminGetTenantParticipantUser")
        ;

        group.MapPost("", async (
            string tenantId,
            [FromBody] CreateTenantParticipantUserRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserTenantService userTenantService,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserEndpoints");
            if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
                return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

            var normalizedRole = NormalizeParticipantRole(body.Role);
            if (normalizedRole == null)
            {
                return Results.BadRequest(new
                {
                    message = $"Role must be {SystemRoles.TenantParticipant} or {SystemRoles.TenantParticipantAdmin}"
                });
            }

            var email = ValidationHelpers.SanitizeAndValidateEmail(body.Email);
            if (email == null)
                return Results.BadRequest(new { message = "Invalid email address" });

            var name = ValidationHelpers.SanitizeString(body.Name);
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Name is required" });

            var dto = new CreateNewUserDto
            {
                Email = email,
                Name = name,
                TenantRoles = new List<string> { normalizedRole }
            };

            var result = await userTenantService.CreateNewUserInTenant(dto, tenantId);
            if (!result.IsSuccess || result.Data == null)
            {
                return Results.Problem(
                    result.ErrorMessage ?? "Create failed",
                    statusCode: (int)result.StatusCode);
            }

            var response = MapToResponse(result.Data, tenantId);
            var location = AdminApiConstants.BuildVersionedPath(
                $"tenants/{Uri.EscapeDataString(tenantId)}/users/{Uri.EscapeDataString(result.Data.UserId)}");
            return response == null
                ? Results.Problem("User created but could not be mapped", statusCode: StatusCodes.Status500InternalServerError)
                : Results.Created(location, response);
        })
        .WithName("AdminCreateTenantParticipantUser")
        ;

        group.MapPatch("/{userId}", async (
            string tenantId,
            string userId,
            [FromBody] UpdateTenantParticipantUserRequest body,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] IRoleCacheService roleCacheService,
            [FromServices] ITokenValidationCache tokenCache,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserEndpoints");
            if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
                return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

            var user = await userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return Results.NotFound(new { message = "User not found" });

            if (user.IsSysAdmin)
            {
                logger.LogWarning("Attempt to modify sys admin user {UserId} via participant endpoint", userId);
                return Results.Forbid();
            }

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tr == null || !HasParticipantRole(tr))
            {
                logger.LogWarning("Patch denied: user {UserId} has no participant role in {TenantId}", userId, tenantId);
                return Results.NotFound(new { message = "User not found in this tenant as a participant user" });
            }

            if (body.Name != null)
            {
                var name = ValidationHelpers.SanitizeString(body.Name);
                if (string.IsNullOrWhiteSpace(name))
                    return Results.BadRequest(new { message = "Name cannot be empty" });
                user.Name = name;
            }

            if (body.Email != null)
            {
                var email = ValidationHelpers.SanitizeAndValidateEmail(body.Email);
                if (email == null)
                    return Results.BadRequest(new { message = "Invalid email address" });
                var other = await userRepository.GetByUserEmailAsync(email);
                if (other != null && !string.Equals(other.UserId, userId, StringComparison.Ordinal))
                    return Results.Conflict(new { message = "Another user already uses this email" });
                user.Email = email;
            }

            if (body.IsApproved.HasValue)
            {
                if (body.IsApproved.Value && user.IsLockedOut)
                    return Results.Problem(
                        detail: "Cannot approve a user that is locked out by system administrator.",
                        statusCode: StatusCodes.Status409Conflict);

                tr!.IsApproved = body.IsApproved.Value;
            }

            if (body.Role != null)
            {
                var nr = NormalizeParticipantRole(body.Role);
                if (nr == null)
                {
                    return Results.BadRequest(new
                    {
                        message = $"Role must be {SystemRoles.TenantParticipant} or {SystemRoles.TenantParticipantAdmin}"
                    });
                }

                tr!.Roles.RemoveAll(r =>
                    r == SystemRoles.TenantParticipant || r == SystemRoles.TenantParticipantAdmin);
                if (!tr.Roles.Contains(nr))
                    tr.Roles.Add(nr);
            }

            var updated = await userRepository.UpdateAsync(userId, user);
            if (!updated)
                return Results.Problem("Update failed", statusCode: StatusCodes.Status500InternalServerError);

            roleCacheService.InvalidateUserRoles(userId, tenantId);
            await tokenCache.InvalidateUserTokens(userId);

            var response = MapToResponse(user, tenantId);
            return response == null
                ? Results.Problem("Updated but could not map response", statusCode: StatusCodes.Status500InternalServerError)
                : Results.Ok(response);
        })
        .WithName("AdminUpdateTenantParticipantUser")
        ;

        group.MapDelete("/{userId}", async (
            string tenantId,
            string userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] IRoleCacheService roleCacheService,
            [FromServices] ITokenValidationCache tokenCache,
            [FromServices] ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AdminUserEndpoints");
            if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
                return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

            var user = await userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return Results.NotFound(new { message = "User not found" });

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (!HasParticipantRoleInTenant(tr))
            {
                logger.LogWarning("Delete denied: user {UserId} has no participant role in {TenantId}", userId, tenantId);
                return Results.NotFound(new { message = "User not found in this tenant as a participant user" });
            }

            if (user.IsSysAdmin)
            {
                logger.LogWarning("Attempt to delete sys admin user {UserId} via participant endpoint", userId);
                return Results.Forbid();
            }

            tr!.Roles.RemoveAll(r =>
                r == SystemRoles.TenantParticipant || r == SystemRoles.TenantParticipantAdmin);
            if (tr.Roles.Count == 0)
                user.TenantRoles.RemoveAll(t => t.Tenant == tenantId);
            var ok = await userRepository.UpdateAsync(userId, user);
            if (!ok)
                return Results.Problem("Failed to remove tenant membership", statusCode: StatusCodes.Status500InternalServerError);

            roleCacheService.InvalidateUserRoles(userId, tenantId);
            await tokenCache.InvalidateUserTokens(userId);

            return Results.NoContent();
        })
        .WithName("AdminDeleteTenantParticipantUser")
        ;
    }

    private static bool TenantRouteMatchesContext(ITenantContext ctx, string tenantIdFromRoute, ILogger logger)
    {
        if (string.IsNullOrEmpty(tenantIdFromRoute) || string.IsNullOrEmpty(ctx.TenantId))
        {
            logger.LogWarning("Missing tenant on route or context");
            return false;
        }

        if (!string.Equals(tenantIdFromRoute, ctx.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Tenant route {RouteTenant} does not match resolved tenant context {CtxTenant}",
                tenantIdFromRoute, ctx.TenantId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Approved tenant role that includes TenantParticipant or TenantParticipantAdmin (other roles may be present).
    /// </summary>
    private static bool HasParticipantRoleInTenant(TenantRole? tr)
    {
        if (tr == null || !tr.IsApproved)
            return false;
        return tr.Roles.Contains(SystemRoles.TenantParticipant) ||
               tr.Roles.Contains(SystemRoles.TenantParticipantAdmin);
    }

    /// <summary>
    /// Checks only that the tenant role contains TenantParticipant or TenantParticipantAdmin,
    /// regardless of approval status. Used for mapping — approval is reflected in the response field.
    /// </summary>
    private static bool HasParticipantRole(TenantRole tr)
    {
        return tr.Roles.Contains(SystemRoles.TenantParticipant) ||
               tr.Roles.Contains(SystemRoles.TenantParticipantAdmin);
    }

    private static string? NormalizeParticipantRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        if (string.Equals(role, SystemRoles.TenantParticipant, StringComparison.OrdinalIgnoreCase))
            return SystemRoles.TenantParticipant;
        if (string.Equals(role, SystemRoles.TenantParticipantAdmin, StringComparison.OrdinalIgnoreCase))
            return SystemRoles.TenantParticipantAdmin;
        return null;
    }

    private static TenantParticipantUserResponse? MapToResponse(User user, string tenantId)
    {
        var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        if (tr == null || !HasParticipantRole(tr))
            return null;

        var role = tr.Roles.Contains(SystemRoles.TenantParticipantAdmin)
            ? SystemRoles.TenantParticipantAdmin
            : SystemRoles.TenantParticipant;

        return new TenantParticipantUserResponse
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            Role = role,
            IsApproved = !user.IsLockedOut && tr.IsApproved
        };
    }
}
