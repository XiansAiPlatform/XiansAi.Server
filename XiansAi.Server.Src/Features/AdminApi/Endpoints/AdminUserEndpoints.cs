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
/// AdminApi CRUD for managing the users of a tenant.
/// Supports create/read/update/delete, enable/disable (account lockout) and changing a
/// user's role to any of the roles defined in <see cref="SystemRoles"/>.
/// Callers must use an API key whose owner is <see cref="SystemRoles.TenantAdmin"/> for the tenant
/// or <see cref="SystemRoles.SysAdmin"/>. Only a <see cref="SystemRoles.SysAdmin"/> may grant the
/// <see cref="SystemRoles.SysAdmin"/> role or modify a user who is already a system administrator.
/// </summary>
public static class AdminUserEndpoints
{
    private const string LoggerCategory = "AdminUserEndpoints";

    /// <summary>Tenant-scoped roles that live on <see cref="TenantRole.Roles"/> (everything except the global SysAdmin).</summary>
    private static readonly string[] TenantScopedRoles =
    {
        SystemRoles.TenantAdmin,
        SystemRoles.TenantUser,
        SystemRoles.TenantParticipant,
        SystemRoles.TenantParticipantAdmin
    };

    public sealed class TenantUserResponse
    {
        [JsonPropertyName("userId")]
        public required string UserId { get; init; }

        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        /// <summary>Tenant roles held by the user in this tenant.</summary>
        [JsonPropertyName("roles")]
        public required List<string> Roles { get; init; }

        /// <summary>True when the user is a global system administrator.</summary>
        [JsonPropertyName("isSysAdmin")]
        public required bool IsSysAdmin { get; init; }

        /// <summary>Tenant membership approval flag.</summary>
        [JsonPropertyName("isApproved")]
        public required bool IsApproved { get; init; }

        /// <summary>False when the account is locked out (disabled).</summary>
        [JsonPropertyName("isEnabled")]
        public required bool IsEnabled { get; init; }
    }

    public sealed class CreateTenantUserRequest
    {
        [JsonPropertyName("email")]
        public required string Email { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        /// <summary>One of the tenant-scoped roles in <see cref="SystemRoles"/> (not SysAdmin).</summary>
        [JsonPropertyName("role")]
        public required string Role { get; init; }
    }

    public sealed class UpdateTenantUserRequest
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("email")]
        public string? Email { get; init; }

        /// <summary>When set, replaces the user's role with this single role (see <see cref="SystemRoles"/>).</summary>
        [JsonPropertyName("role")]
        public string? Role { get; init; }

        [JsonPropertyName("isApproved")]
        public bool? IsApproved { get; init; }
    }

    public sealed class ChangeRoleRequest
    {
        /// <summary>Target role, one of the roles defined in <see cref="SystemRoles"/>.</summary>
        [JsonPropertyName("role")]
        public required string Role { get; init; }
    }

    public sealed class DisableUserRequest
    {
        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }

    public static void MapAdminUserEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/users")
            .WithTags("AdminAPI - Tenant Users")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        group.MapGet("", ListUsers).WithName("AdminListTenantUsers");
        group.MapGet("/{userId}", GetUser).WithName("AdminGetTenantUser");
        group.MapPost("", CreateUser).WithName("AdminCreateTenantUser");
        group.MapPatch("/{userId}", UpdateUser).WithName("AdminUpdateTenantUser");
        group.MapPut("/{userId}/role", ChangeRole).WithName("AdminChangeTenantUserRole");
        group.MapPut("/{userId}/enable", EnableUser).WithName("AdminEnableTenantUser");
        group.MapPut("/{userId}/disable", DisableUser).WithName("AdminDisableTenantUser");
        group.MapDelete("/{userId}", DeleteUser).WithName("AdminDeleteTenantUser");
    }

    private static async Task<IResult> ListUsers(
        string tenantId,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var filter = new UserFilter
        {
            Page = page > 0 ? page : 1,
            PageSize = pageSize > 0 ? pageSize : 20,
            Type = MapRoleToFilter(role),
            Tenant = tenantId,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()
        };

        try
        {
            var paged = await userRepository.GetAllUsersByTenantAsync(filter);
            var items = paged.Users.Select(u => MapToResponse(u, tenantId)).ToList();

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
            logger.LogError(ex, "Error listing users for tenant {TenantId}", tenantId);
            return Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetUser(
        string tenantId,
        string userId,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, _, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        return Results.Ok(MapToResponse(user!, tenantId));
    }

    private static async Task<IResult> CreateUser(
        string tenantId,
        [FromBody] CreateTenantUserRequest body,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserTenantService userTenantService,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var normalizedRole = NormalizeRole(body.Role);
        if (normalizedRole == null)
            return InvalidRoleResult();
        if (normalizedRole == SystemRoles.SysAdmin)
            return Results.BadRequest(new
            {
                message = $"{SystemRoles.SysAdmin} cannot be assigned on creation. Create the user then change the role."
            });

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
            return Results.Problem(result.ErrorMessage ?? "Create failed", statusCode: (int)result.StatusCode);

        var location = AdminApiConstants.BuildVersionedPath(
            $"tenants/{Uri.EscapeDataString(tenantId)}/users/{Uri.EscapeDataString(result.Data.UserId)}");
        return Results.Created(location, MapToResponse(result.Data, tenantId));
    }

    private static async Task<IResult> UpdateUser(
        string tenantId,
        string userId,
        [FromBody] UpdateTenantUserRequest body,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] IRoleCacheService roleCacheService,
        [FromServices] ITokenValidationCache tokenCache,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, tr, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        var guard = GuardSysAdminTarget(user!, tenantContext, logger, "update");
        if (guard != null)
            return guard;

        if (body.Name != null)
        {
            var name = ValidationHelpers.SanitizeString(body.Name);
            if (string.IsNullOrWhiteSpace(name))
                return Results.BadRequest(new { message = "Name cannot be empty" });
            user!.Name = name;
        }

        if (body.Email != null)
        {
            var emailResult = await ApplyEmailChange(userRepository, user!, body.Email);
            if (emailResult != null)
                return emailResult;
        }

        if (body.IsApproved.HasValue)
        {
            if (body.IsApproved.Value && user!.IsLockedOut)
                return Results.Problem(
                    detail: "Cannot approve a user that is disabled (locked out).",
                    statusCode: StatusCodes.Status409Conflict);
            tr!.IsApproved = body.IsApproved.Value;
        }

        if (body.Role != null)
        {
            var roleResult = ApplyRoleChange(user!, tr!, body.Role, CallerIsSysAdmin(tenantContext));
            if (roleResult != null)
                return roleResult;
        }

        return await SaveAndInvalidate(userRepository, roleCacheService, tokenCache, user!, tenantId);
    }

    private static async Task<IResult> ChangeRole(
        string tenantId,
        string userId,
        [FromBody] ChangeRoleRequest body,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] IRoleCacheService roleCacheService,
        [FromServices] ITokenValidationCache tokenCache,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, tr, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        var guard = GuardSysAdminTarget(user!, tenantContext, logger, "change role of");
        if (guard != null)
            return guard;

        var roleResult = ApplyRoleChange(user!, tr!, body.Role, CallerIsSysAdmin(tenantContext));
        if (roleResult != null)
            return roleResult;

        return await SaveAndInvalidate(userRepository, roleCacheService, tokenCache, user!, tenantId);
    }

    private static async Task<IResult> EnableUser(
        string tenantId,
        string userId,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] ITokenValidationCache tokenCache,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, _, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        var guard = GuardSysAdminTarget(user!, tenantContext, logger, "enable");
        if (guard != null)
            return guard;

        var unlocked = await userRepository.UnlockUserAsync(userId);
        if (!unlocked)
            return Results.Problem("Failed to enable user", statusCode: StatusCodes.Status500InternalServerError);

        await tokenCache.InvalidateUserTokens(userId);
        logger.LogInformation("User {UserId} enabled by {Admin} in tenant {TenantId}", userId, tenantContext.LoggedInUser, tenantId);

        var refreshed = await userRepository.GetByUserIdAsync(userId);
        return Results.Ok(MapToResponse(refreshed ?? user!, tenantId));
    }

    private static async Task<IResult> DisableUser(
        string tenantId,
        string userId,
        [FromBody] DisableUserRequest? body,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] ITokenValidationCache tokenCache,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, _, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        var guard = GuardSysAdminTarget(user!, tenantContext, logger, "disable");
        if (guard != null)
            return guard;

        var reason = ValidationHelpers.SanitizeString(body?.Reason);
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Disabled by administrator";

        var lockedBy = tenantContext.LoggedInUser ?? "system";
        var locked = await userRepository.LockUserAsync(userId, reason, lockedBy);
        if (!locked)
            return Results.Problem("Failed to disable user", statusCode: StatusCodes.Status500InternalServerError);

        await tokenCache.InvalidateUserTokens(userId);
        logger.LogInformation("User {UserId} disabled by {Admin} in tenant {TenantId}", userId, tenantContext.LoggedInUser, tenantId);

        var refreshed = await userRepository.GetByUserIdAsync(userId);
        return Results.Ok(MapToResponse(refreshed ?? user!, tenantId));
    }

    private static async Task<IResult> DeleteUser(
        string tenantId,
        string userId,
        [FromServices] ITenantContext tenantContext,
        [FromServices] IUserRepository userRepository,
        [FromServices] IRoleCacheService roleCacheService,
        [FromServices] ITokenValidationCache tokenCache,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        if (!TenantRouteMatchesContext(tenantContext, tenantId, logger))
            return TenantScopeMismatch();

        var (user, _, error) = await ResolveTenantUser(userRepository, tenantId, userId);
        if (error != null)
            return error;

        var guard = GuardSysAdminTarget(user!, tenantContext, logger, "delete");
        if (guard != null)
            return guard;

        user!.TenantRoles.RemoveAll(t => t.Tenant == tenantId);
        var ok = await userRepository.UpdateAsync(userId, user);
        if (!ok)
            return Results.Problem("Failed to remove tenant membership", statusCode: StatusCodes.Status500InternalServerError);

        roleCacheService.InvalidateUserRoles(userId, tenantId);
        await tokenCache.InvalidateUserTokens(userId);

        return Results.NoContent();
    }

    private static async Task<(User? User, TenantRole? Tr, IResult? Error)> ResolveTenantUser(
        IUserRepository userRepository, string tenantId, string userId)
    {
        var user = await userRepository.GetByUserIdAsync(userId);
        if (user == null)
            return (null, null, Results.NotFound(new { message = "User not found" }));

        var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        if (tr == null)
            return (null, null, Results.NotFound(new { message = "User is not a member of this tenant" }));

        return (user, tr, null);
    }

    /// <summary>
    /// Applies a role change. SysAdmin is a global flag and may only be granted by a SysAdmin caller.
    /// Any other (tenant-scoped) role replaces the user's roles for this tenant. Returns null on success
    /// or an error <see cref="IResult"/> when the role is invalid or not permitted.
    /// </summary>
    private static IResult? ApplyRoleChange(User user, TenantRole tr, string role, bool callerIsSysAdmin)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole == null)
            return InvalidRoleResult();

        if (normalizedRole == SystemRoles.SysAdmin)
        {
            if (!callerIsSysAdmin)
                return Results.Json(
                    new { message = $"Only system administrators can grant the {SystemRoles.SysAdmin} role" },
                    statusCode: StatusCodes.Status403Forbidden);
            user.IsSysAdmin = true;
            return null;
        }

        tr.Roles.Clear();
        tr.Roles.Add(normalizedRole);

        // A SysAdmin can demote a system administrator by assigning a tenant-scoped role.
        // Tenant admins never reach this point for sys admin targets (blocked by GuardSysAdminTarget).
        if (callerIsSysAdmin && user.IsSysAdmin)
            user.IsSysAdmin = false;

        return null;
    }

    private static async Task<IResult> ApplyEmailChange(IUserRepository userRepository, User user, string emailInput)
    {
        var email = ValidationHelpers.SanitizeAndValidateEmail(emailInput);
        if (email == null)
            return Results.BadRequest(new { message = "Invalid email address" });

        var other = await userRepository.GetByUserEmailAsync(email);
        if (other != null && !string.Equals(other.UserId, user.UserId, StringComparison.Ordinal))
            return Results.Conflict(new { message = "Another user already uses this email" });

        user.Email = email;
        return null!;
    }

    private static async Task<IResult> SaveAndInvalidate(
        IUserRepository userRepository,
        IRoleCacheService roleCacheService,
        ITokenValidationCache tokenCache,
        User user,
        string tenantId)
    {
        var updated = await userRepository.UpdateAsync(user.UserId, user);
        if (!updated)
            return Results.Problem("Update failed", statusCode: StatusCodes.Status500InternalServerError);

        roleCacheService.InvalidateUserRoles(user.UserId, tenantId);
        await tokenCache.InvalidateUserTokens(user.UserId);

        return Results.Ok(MapToResponse(user, tenantId));
    }

    private static IResult? GuardSysAdminTarget(User user, ITenantContext tenantContext, ILogger logger, string action)
    {
        if (user.IsSysAdmin && !CallerIsSysAdmin(tenantContext))
        {
            logger.LogWarning("Non-sysadmin attempted to {Action} system administrator {UserId}", action, user.UserId);
            return Results.Json(
                new { message = "Only system administrators can modify a system administrator" },
                statusCode: StatusCodes.Status403Forbidden);
        }
        return null;
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

    private static bool CallerIsSysAdmin(ITenantContext ctx)
    {
        return ctx.UserRoles?.Contains(SystemRoles.SysAdmin) == true;
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        if (string.Equals(role, SystemRoles.SysAdmin, StringComparison.OrdinalIgnoreCase))
            return SystemRoles.SysAdmin;
        return TenantScopedRoles.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
    }

    private static UserTypeFilter MapRoleToFilter(string? role)
    {
        var normalized = NormalizeRole(role);
        return normalized switch
        {
            SystemRoles.TenantAdmin => UserTypeFilter.ADMIN,
            SystemRoles.TenantUser => UserTypeFilter.NON_ADMIN,
            SystemRoles.TenantParticipant => UserTypeFilter.PARTICIPANT,
            SystemRoles.TenantParticipantAdmin => UserTypeFilter.PARTICIPANT_ADMIN,
            _ => UserTypeFilter.ALL
        };
    }

    private static IResult InvalidRoleResult()
    {
        var allRoles = new[] { SystemRoles.SysAdmin }.Concat(TenantScopedRoles);
        return Results.BadRequest(new { message = $"Role must be one of: {string.Join(", ", allRoles)}" });
    }

    private static IResult TenantScopeMismatch()
    {
        return Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static TenantUserResponse MapToResponse(User user, string tenantId)
    {
        var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        return new TenantUserResponse
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            Roles = tr?.Roles.ToList() ?? new List<string>(),
            IsSysAdmin = user.IsSysAdmin,
            IsApproved = tr?.IsApproved ?? false,
            IsEnabled = !user.IsLockedOut
        };
    }
}
