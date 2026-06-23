using System.Text.Json.Serialization;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// A tenant participant user as exposed by the tenant-scoped admin API.
/// </summary>
public class TenantParticipantUser
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

/// <summary>
/// Paged result envelope for tenant participant users.
/// </summary>
public class PagedParticipantResult
{
    [JsonPropertyName("users")]
    public required List<TenantParticipantUser> Users { get; init; }
    [JsonPropertyName("totalCount")]
    public required long TotalCount { get; init; }
    [JsonPropertyName("page")]
    public required int Page { get; init; }
    [JsonPropertyName("pageSize")]
    public required int PageSize { get; init; }
}

/// <summary>
/// Tenant-scoped management of users that hold <see cref="SystemRoles.TenantParticipant"/> or
/// <see cref="SystemRoles.TenantParticipantAdmin"/> in a tenant.
/// Tenant authorization (route vs resolved context) is enforced at the endpoint layer;
/// this service owns the participant business rules and persistence.
/// </summary>
public interface ITenantParticipantUserService
{
    Task<ServiceResult<PagedParticipantResult>> ListAsync(string tenantId, int page, int pageSize, string? search);
    Task<ServiceResult<TenantParticipantUser>> GetAsync(string tenantId, string userId);
    Task<ServiceResult<TenantParticipantUser>> CreateAsync(string tenantId, string email, string name, string role);
    Task<ServiceResult<TenantParticipantUser>> UpdateAsync(
        string tenantId, string userId, string? name, string? email, string? role, bool? isApproved,
        bool callerIsSysAdmin = false);
    Task<ServiceResult<bool>> DeleteAsync(string tenantId, string userId, bool callerIsSysAdmin = false);
    /// <summary>
    /// Removes a single role from a user's tenant membership.
    /// If the user has no remaining roles in the tenant the membership is removed entirely.
    /// </summary>
    Task<ServiceResult<bool>> RemoveRoleAsync(string tenantId, string userId, string role, bool callerIsSysAdmin = false);
}

public class TenantParticipantUserService : ITenantParticipantUserService
{
    private const int DefaultPageSize = 20;

    private readonly IUserRepository _userRepository;
    private readonly IUserTenantService _userTenantService;
    private readonly IRoleCacheService _roleCacheService;
    private readonly ITokenValidationCache _tokenCache;
    private readonly ILogger<TenantParticipantUserService> _logger;

    public TenantParticipantUserService(
        IUserRepository userRepository,
        IUserTenantService userTenantService,
        IRoleCacheService roleCacheService,
        ITokenValidationCache tokenCache,
        ILogger<TenantParticipantUserService> logger)
    {
        _userRepository = userRepository;
        _userTenantService = userTenantService;
        _roleCacheService = roleCacheService;
        _tokenCache = tokenCache;
        _logger = logger;
    }

    public async Task<ServiceResult<PagedParticipantResult>> ListAsync(string tenantId, int page, int pageSize, string? search)
    {
        try
        {
            var filter = new UserFilter
            {
                Page = page > 0 ? page : 1,
                PageSize = pageSize > 0 ? pageSize : DefaultPageSize,
                Type = UserTypeFilter.ALL,
                Tenant = tenantId,
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            };

            var paged = await _userRepository.GetAllUsersByTenantAsync(filter);
            var users = paged.Users
                .Select(u => MapToTenantUser(u, tenantId))
                .Where(p => p != null)
                .Cast<TenantParticipantUser>()
                .ToList();

            return ServiceResult<PagedParticipantResult>.Success(new PagedParticipantResult
            {
                Users = users,
                TotalCount = paged.TotalCount,
                Page = filter.Page,
                PageSize = filter.PageSize,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing participant users for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<PagedParticipantResult>.InternalServerError("An error occurred while listing participant users");
        }
    }

    public async Task<ServiceResult<TenantParticipantUser>> GetAsync(string tenantId, string userId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<TenantParticipantUser>.NotFound("User not found");

            var mapped = MapToTenantUser(user, tenantId);
            if (mapped == null)
                return ServiceResult<TenantParticipantUser>.NotFound("User not found in this tenant");

            return ServiceResult<TenantParticipantUser>.Success(mapped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving participant user {UserId} in tenant {TenantId}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantParticipantUser>.InternalServerError("An error occurred while retrieving the participant user");
        }
    }

    public async Task<ServiceResult<TenantParticipantUser>> CreateAsync(string tenantId, string email, string name, string role)
    {
        var normalizedRole = NormalizeTenantRole(role);
        if (normalizedRole == null)
            return ServiceResult<TenantParticipantUser>.BadRequest(
                $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");

        var sanitizedEmail = ValidationHelpers.SanitizeAndValidateEmail(email);
        if (sanitizedEmail == null)
            return ServiceResult<TenantParticipantUser>.BadRequest("Invalid email address");

        var sanitizedName = ValidationHelpers.SanitizeString(name);
        if (string.IsNullOrWhiteSpace(sanitizedName))
            return ServiceResult<TenantParticipantUser>.BadRequest("Name is required");

        // If the user already exists, add them to the tenant instead of creating a new account.
        var existingUser = await _userRepository.GetByUserEmailAsync(sanitizedEmail);
        if (existingUser != null)
            return await AddTenantToExistingUserAsync(existingUser, tenantId, normalizedRole);

        var dto = new CreateNewUserDto
        {
            Email = sanitizedEmail,
            Name = sanitizedName,
            TenantRoles = new List<string> { normalizedRole },
        };

        var result = await _userTenantService.CreateNewUserInTenant(dto, tenantId);
        if (!result.IsSuccess || result.Data == null)
            return ServiceResult<TenantParticipantUser>.BadRequest(
                result.ErrorMessage ?? "Create failed", result.StatusCode);

        var created = result.Data;
        var tenantRole = created.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        return ServiceResult<TenantParticipantUser>.Success(new TenantParticipantUser
        {
            UserId = created.UserId,
            Email = created.Email,
            Name = created.Name,
            Role = normalizedRole,
            IsApproved = !created.IsLockedOut && (tenantRole?.IsApproved ?? true),
        }, StatusCode.Created);
    }

    private async Task<ServiceResult<TenantParticipantUser>> AddTenantToExistingUserAsync(
        User user, string tenantId, string normalizedRole)
    {
        var existingMembership = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        if (existingMembership != null)
        {
            if (existingMembership.Roles.Contains(normalizedRole))
                return ServiceResult<TenantParticipantUser>.Conflict(
                    $"User already has role '{normalizedRole}' in this tenant");

            // User is in the tenant but missing this specific role — add it.
            existingMembership.Roles.Add(normalizedRole);
        }
        else
        {
            user.TenantRoles.Add(new Data.Models.TenantRole
            {
                Tenant = tenantId,
                Roles = new List<string> { normalizedRole },
                IsApproved = true,
            });
        }

        var ok = await _userRepository.UpdateAsync(user.UserId, user);
        if (!ok)
            return ServiceResult<TenantParticipantUser>.InternalServerError("Failed to add user to tenant");

        await InvalidateCachesAsync(user.UserId, tenantId);

        _logger.LogInformation("Existing user {UserId} added to tenant {TenantId} with role {Role}",
            LogSanitizer.Sanitize(user.UserId), LogSanitizer.Sanitize(tenantId), normalizedRole);

        return ServiceResult<TenantParticipantUser>.Success(new TenantParticipantUser
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            Role = normalizedRole,
            IsApproved = !user.IsLockedOut,
        }, StatusCode.Created);
    }

    public async Task<ServiceResult<TenantParticipantUser>> UpdateAsync(
        string tenantId, string userId, string? name, string? email, string? role, bool? isApproved,
        bool callerIsSysAdmin = false)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<TenantParticipantUser>.NotFound("User not found");

            if (user.IsSysAdmin && !callerIsSysAdmin)
            {
                _logger.LogWarning("Attempt to modify sys admin user {UserId} via participant service", LogSanitizer.Sanitize(userId));
                return ServiceResult<TenantParticipantUser>.Forbidden("Cannot modify a system administrator via this endpoint");
            }

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tr == null)
                return ServiceResult<TenantParticipantUser>.NotFound("User not found in this tenant");

            if (name != null)
            {
                var sanitized = ValidationHelpers.SanitizeString(name);
                if (string.IsNullOrWhiteSpace(sanitized))
                    return ServiceResult<TenantParticipantUser>.BadRequest("Name cannot be empty");
                user.Name = sanitized;
            }

            if (email != null)
            {
                var sanitizedEmail = ValidationHelpers.SanitizeAndValidateEmail(email);
                if (sanitizedEmail == null)
                    return ServiceResult<TenantParticipantUser>.BadRequest("Invalid email address");

                var other = await _userRepository.GetByUserEmailAsync(sanitizedEmail);
                if (other != null && !string.Equals(other.UserId, userId, StringComparison.Ordinal))
                    return ServiceResult<TenantParticipantUser>.Conflict("Another user already uses this email");
                user.Email = sanitizedEmail;
            }

            if (isApproved.HasValue)
            {
                if (isApproved.Value && user.IsLockedOut)
                    return ServiceResult<TenantParticipantUser>.Conflict("Cannot approve a user that is locked out by system administrator.");
                tr.IsApproved = isApproved.Value;
            }

            if (role != null)
            {
                var normalizedRole = NormalizeTenantRole(role);
                if (normalizedRole == null)
                    return ServiceResult<TenantParticipantUser>.BadRequest(
                        $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");

                if (!tr.Roles.Contains(normalizedRole))
                    tr.Roles.Add(normalizedRole);
            }

            var updated = await _userRepository.UpdateAsync(userId, user);
            if (!updated)
                return ServiceResult<TenantParticipantUser>.InternalServerError("Update failed");

            await InvalidateCachesAsync(userId, tenantId);

            var mapped = MapToTenantUser(user, tenantId);
            if (mapped == null)
                return ServiceResult<TenantParticipantUser>.InternalServerError("Updated but could not map response");

            return ServiceResult<TenantParticipantUser>.Success(mapped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating participant user {UserId} in tenant {TenantId}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantParticipantUser>.InternalServerError("An error occurred while updating the participant user");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string tenantId, string userId, bool callerIsSysAdmin = false)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tr == null)
                return ServiceResult<bool>.NotFound("User not found in this tenant");

            if (user.IsSysAdmin && !callerIsSysAdmin)
            {
                _logger.LogWarning("Attempt to delete sys admin user {UserId} via participant service", LogSanitizer.Sanitize(userId));
                return ServiceResult<bool>.Forbidden("Cannot delete a system administrator via this endpoint");
            }

            user.TenantRoles.RemoveAll(t => t.Tenant == tenantId);

            var ok = await _userRepository.UpdateAsync(userId, user);
            if (!ok)
                return ServiceResult<bool>.InternalServerError("Failed to remove tenant membership");

            await InvalidateCachesAsync(userId, tenantId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting participant user {UserId} in tenant {TenantId}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the participant user");
        }
    }

    public async Task<ServiceResult<bool>> RemoveRoleAsync(string tenantId, string userId, string role, bool callerIsSysAdmin = false)
    {
        try
        {
            var normalizedRole = NormalizeTenantRole(role);
            if (normalizedRole == null)
                return ServiceResult<bool>.BadRequest(
                    $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            if (user.IsSysAdmin && !callerIsSysAdmin)
            {
                _logger.LogWarning("Attempt to remove role from sys admin user {UserId} via participant service",
                    LogSanitizer.Sanitize(userId));
                return ServiceResult<bool>.Forbidden("Cannot modify a system administrator via this endpoint");
            }

            var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
            if (tr == null)
                return ServiceResult<bool>.NotFound("User has no membership in this tenant");

            if (!tr.Roles.Contains(normalizedRole))
                return ServiceResult<bool>.NotFound($"User does not have role '{normalizedRole}' in this tenant");

            tr.Roles.Remove(normalizedRole);

            if (tr.Roles.Count == 0)
                user.TenantRoles.RemoveAll(t => t.Tenant == tenantId);

            var ok = await _userRepository.UpdateAsync(userId, user);
            if (!ok)
                return ServiceResult<bool>.InternalServerError("Failed to remove role");

            await InvalidateCachesAsync(userId, tenantId);

            _logger.LogInformation("Role {Role} removed from user {UserId} in tenant {TenantId}",
                normalizedRole, LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing role from user {UserId} in tenant {TenantId}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("An error occurred while removing the role");
        }
    }

    private async Task InvalidateCachesAsync(string userId, string tenantId)
    {
        _roleCacheService.InvalidateUserRoles(userId, tenantId);
        await _tokenCache.InvalidateUserTokens(userId);
    }

    /// <summary>Tenant role containing a participant role, regardless of approval status.</summary>
    private static bool HasParticipantRole(TenantRole tr)
    {
        return tr.Roles.Contains(SystemRoles.TenantParticipant) ||
               tr.Roles.Contains(SystemRoles.TenantParticipantAdmin);
    }

    /// <summary>Approved tenant role that includes a participant role.</summary>
    private static bool HasApprovedParticipantRole(TenantRole? tr)
    {
        return tr != null && tr.IsApproved && HasParticipantRole(tr);
    }

    /// <summary>
    /// Tenant roles ordered highest to lowest privilege: TenantAdmin > TenantUser > TenantParticipantAdmin > TenantParticipant.
    /// Used for validation and for picking the primary display role.
    /// </summary>
    private static readonly string[] AllowedTenantRoles =
    {
        SystemRoles.TenantAdmin,
        SystemRoles.TenantUser,
        SystemRoles.TenantParticipantAdmin,
        SystemRoles.TenantParticipant,
    };

    /// <summary>Case-insensitively matches the input against an allowed tenant role, returning its canonical form.</summary>
    private static string? NormalizeTenantRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        var trimmed = role.Trim();
        return AllowedTenantRoles.FirstOrDefault(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Maps a user to the tenant user response. Returns null if the user has no membership in the tenant.
    /// The Role field reflects the highest-privilege role the user holds in the tenant.
    /// </summary>
    private static TenantParticipantUser? MapToTenantUser(User user, string tenantId)
    {
        var tr = user.TenantRoles.FirstOrDefault(t => t.Tenant == tenantId);
        if (tr == null)
            return null;

        return new TenantParticipantUser
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            Role = PrimaryRole(tr.Roles),
            IsApproved = !user.IsLockedOut && tr.IsApproved,
        };
    }

    /// <summary>Returns the highest-privilege role from a list, falling back to the first entry.</summary>
    private static string PrimaryRole(List<string> roles)
    {
        foreach (var candidate in AllowedTenantRoles)
        {
            if (roles.Contains(candidate))
                return candidate;
        }
        return roles.FirstOrDefault() ?? string.Empty;
    }
}
