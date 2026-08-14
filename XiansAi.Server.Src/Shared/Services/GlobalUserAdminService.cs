using System.Text.Json.Serialization;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Lightweight summary of a user for the tenant-independent admin list view.
/// </summary>
public class GlobalUserSummary
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }
    [JsonPropertyName("email")]
    public required string Email { get; init; }
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    [JsonPropertyName("isSysAdmin")]
    public required bool IsSysAdmin { get; init; }
    [JsonPropertyName("isEnabled")]
    public required bool IsEnabled { get; init; }
    [JsonPropertyName("tenantCount")]
    public required int TenantCount { get; init; }
    [JsonPropertyName("providerAuthority")]
    public string? ProviderAuthority { get; init; }
    [JsonPropertyName("isLockedOut")]
    public bool IsLockedOut { get; init; }
    [JsonPropertyName("lockedOutReason")]
    public string? LockedOutReason { get; init; }
    [JsonPropertyName("lockedOutAt")]
    public DateTime? LockedOutAt { get; init; }
    [JsonPropertyName("lockedOutBy")]
    public string? LockedOutBy { get; init; }
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// A single tenant membership of a user, including the resolved tenant name.
/// </summary>
public class GlobalUserMembership
{
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; init; }
    [JsonPropertyName("tenantName")]
    public required string TenantName { get; init; }
    [JsonPropertyName("roles")]
    public required List<string> Roles { get; init; }
    [JsonPropertyName("isApproved")]
    public required bool IsApproved { get; init; }
}

/// <summary>
/// Full user profile with all tenant memberships for the admin detail view.
/// </summary>
public class GlobalUserDetail
{
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }
    [JsonPropertyName("email")]
    public required string Email { get; init; }
    [JsonPropertyName("name")]
    public required string Name { get; init; }
    [JsonPropertyName("isSysAdmin")]
    public required bool IsSysAdmin { get; init; }
    [JsonPropertyName("isEnabled")]
    public required bool IsEnabled { get; init; }
    /// <summary>
    /// Why the account is disabled, for whoever has to decide whether to enable it. An account
    /// created because its address is also a system administrator's says so here, along with what
    /// enabling it on its own would cost — which is not otherwise visible from this record.
    /// </summary>
    [JsonPropertyName("disabledReason")]
    public string? DisabledReason { get; init; }
    [JsonPropertyName("providerAuthority")]
    public string? ProviderAuthority { get; init; }
    [JsonPropertyName("isLockedOut")]
    public bool IsLockedOut { get; init; }
    [JsonPropertyName("lockedOutReason")]
    public string? LockedOutReason { get; init; }
    [JsonPropertyName("lockedOutAt")]
    public DateTime? LockedOutAt { get; init; }
    [JsonPropertyName("lockedOutBy")]
    public string? LockedOutBy { get; init; }
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }
    [JsonPropertyName("memberships")]
    public required List<GlobalUserMembership> Memberships { get; init; }
}

/// <summary>
/// Paged result envelope for the tenant-independent user list.
/// </summary>
public class GlobalUserListResult
{
    [JsonPropertyName("users")]
    public required List<GlobalUserSummary> Users { get; init; }
    [JsonPropertyName("totalCount")]
    public required long TotalCount { get; init; }
    [JsonPropertyName("page")]
    public required int Page { get; init; }
    [JsonPropertyName("pageSize")]
    public required int PageSize { get; init; }
}

/// <summary>
/// Tenant-independent (global) user administration.
/// Authorization is enforced at the endpoint/policy layer; this service contains
/// no tenant-context coupling so it can serve any System Admin caller generically.
/// </summary>
public interface IGlobalUserAdminService
{
    Task<ServiceResult<GlobalUserListResult>> ListUsersAsync(UserFilter filter);
    Task<ServiceResult<GlobalUserDetail>> GetUserWithMembershipsAsync(string userId);
    Task<ServiceResult<GlobalUserDetail>> UpdateProfileAsync(string userId, string? name, string? email);
    Task<ServiceResult<GlobalUserDetail>> SetSysAdminAsync(string userId, bool isSysAdmin);
    Task<ServiceResult<GlobalUserDetail>> SetStatusAsync(string userId, bool enabled, string? reason, string actingUserId);
    Task<ServiceResult<bool>> DeleteUserAsync(string userId, string actingUserId);
}

public class GlobalUserAdminService : IGlobalUserAdminService
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private static readonly string[] AllowedTenantRoles =
    {
        SystemRoles.TenantAdmin,
        SystemRoles.TenantUser,
        SystemRoles.TenantParticipantAdmin,
        SystemRoles.TenantParticipant,
    };

    private readonly IUserRepository _userRepository;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly IUserAuthorizationInvalidator _authorizationInvalidator;
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly ILogger<GlobalUserAdminService> _logger;

    public GlobalUserAdminService(
        IUserRepository userRepository,
        ITenantCacheService tenantCacheService,
        IUserAuthorizationInvalidator authorizationInvalidator,
        IWebhookEventPublisher webhookEventPublisher,
        ILogger<GlobalUserAdminService> logger)
    {
        _userRepository = userRepository;
        _tenantCacheService = tenantCacheService;
        _authorizationInvalidator = authorizationInvalidator;
        _webhookEventPublisher = webhookEventPublisher;
        _logger = logger;
    }

    public async Task<ServiceResult<GlobalUserListResult>> ListUsersAsync(UserFilter filter)
    {
        try
        {
            // role=SysAdmin is a global flag rather than a tenant role, so it maps to IsSysAdmin.
            string? normalizedRole = null;
            var isSysAdmin = filter.IsSysAdmin;
            if (!string.IsNullOrWhiteSpace(filter.Role))
            {
                var trimmed = filter.Role.Trim();
                if (string.Equals(trimmed, SystemRoles.SysAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    isSysAdmin = true;
                }
                else
                {
                    normalizedRole = AllowedTenantRoles.FirstOrDefault(
                        r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase));
                    if (normalizedRole == null)
                        return ServiceResult<GlobalUserListResult>.BadRequest(
                            $"Role must be one of: {SystemRoles.SysAdmin}, {string.Join(", ", AllowedTenantRoles)}");
                }
            }

            var normalized = new UserFilter
            {
                Page = filter.Page > 0 ? filter.Page : 1,
                PageSize = Math.Min(filter.PageSize > 0 ? filter.PageSize : DefaultPageSize, MaxPageSize),
                Type = UserTypeFilter.ALL,
                Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
                IsSysAdmin = isSysAdmin,
                IsEnabled = filter.IsEnabled,
                Role = normalizedRole,
            };

            var paged = await _userRepository.GetAllUsersAsync(normalized);
            var users = paged.Users.Select(ToSummary).ToList();

            return ServiceResult<GlobalUserListResult>.Success(new GlobalUserListResult
            {
                Users = users,
                TotalCount = paged.TotalCount,
                Page = normalized.Page,
                PageSize = normalized.PageSize,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing global users");
            return ServiceResult<GlobalUserListResult>.InternalServerError("An error occurred while listing users");
        }
    }

    public async Task<ServiceResult<GlobalUserDetail>> GetUserWithMembershipsAsync(string userId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving global user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while retrieving the user");
        }
    }

    public async Task<ServiceResult<GlobalUserDetail>> UpdateProfileAsync(string userId, string? name, string? email)
    {
        try
        {
            if (name == null && email == null)
                return ServiceResult<GlobalUserDetail>.BadRequest("No fields to update");

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            if (name != null)
            {
                var sanitized = ValidationHelpers.SanitizeString(name);
                if (string.IsNullOrWhiteSpace(sanitized))
                    return ServiceResult<GlobalUserDetail>.BadRequest("Name cannot be empty");
                user.Name = sanitized;
            }

            if (email != null)
            {
                var sanitizedEmail = ValidationHelpers.SanitizeAndValidateEmail(email);
                if (sanitizedEmail == null)
                    return ServiceResult<GlobalUserDetail>.BadRequest("Invalid email address");

                var existing = await _userRepository.GetByUserEmailAsync(sanitizedEmail);
                if (existing != null && !string.Equals(existing.UserId, userId, StringComparison.Ordinal))
                    return ServiceResult<GlobalUserDetail>.Conflict("Another user already uses this email");

                user.Email = sanitizedEmail;
            }

            var updated = await _userRepository.UpdateAsync(userId, user);
            if (!updated)
                return ServiceResult<GlobalUserDetail>.InternalServerError("Update failed");

            await InvalidateCachesAsync(user);
            _logger.LogInformation("Global user {UserId} profile updated", LogSanitizer.Sanitize(userId));

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.UserUpdated,
                new { userId = user.UserId, email = user.Email, name = user.Name });

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating global user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while updating the user");
        }
    }

    public async Task<ServiceResult<GlobalUserDetail>> SetSysAdminAsync(string userId, bool isSysAdmin)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            // The one place that accepts a grant on an address several accounts hold. Making it
            // here is the operator stating that those accounts are the same person, which is what
            // lets the address authenticate as a system administrator again. Every other promotion
            // path refuses, so this is the only record of that decision being taken.
            if (isSysAdmin
                && !string.IsNullOrWhiteSpace(user.Email)
                && await _userRepository.IsEmailSharedAsync(user.Email, user.UserId))
            {
                _logger.LogWarning(
                    "Granting SysAdmin to {UserId}, whose email other accounts also hold. That " +
                    "address authenticates as a system administrator only once every enabled " +
                    "account holding it has the role",
                    LogSanitizer.Sanitize(userId));
            }

            var updated = await _userRepository.SetSysAdminAsync(userId, isSysAdmin);
            if (!updated)
                return ServiceResult<GlobalUserDetail>.InternalServerError("Update failed");

            user.IsSysAdmin = isSysAdmin;
            await InvalidateCachesAsync(user);
            _logger.LogInformation("SysAdmin flag for user {UserId} set to {Value}",
                LogSanitizer.Sanitize(userId), isSysAdmin);

            await _webhookEventPublisher.PublishAsync(
                isSysAdmin ? WebhookEventTypes.UserSysAdminGranted : WebhookEventTypes.UserSysAdminRevoked,
                new { userId = user.UserId, email = user.Email, isSysAdmin });

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting SysAdmin flag for user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while updating the user");
        }
    }

    public async Task<ServiceResult<GlobalUserDetail>> SetStatusAsync(string userId, bool enabled, string? reason, string actingUserId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            bool ok;
            if (enabled)
            {
                ok = await _userRepository.UnlockUserAsync(userId);
                if (ok)
                {
                    user.IsLockedOut = false;
                    await WarnIfEnablingDemotesASysAdminAsync(user);
                }
            }
            else
            {
                var lockReason = string.IsNullOrWhiteSpace(reason)
                    ? "Disabled by system administrator"
                    : reason.Trim();
                ok = await _userRepository.LockUserAsync(userId, lockReason, actingUserId);
                if (ok)
                {
                    user.IsLockedOut = true;
                    user.LockedOutReason = lockReason;
                    user.LockedOutBy = actingUserId;
                }
            }

            if (!ok)
                return ServiceResult<GlobalUserDetail>.InternalServerError("Status update failed");

            await InvalidateCachesAsync(user);
            _logger.LogInformation("User {UserId} {Action}",
                LogSanitizer.Sanitize(userId), enabled ? "enabled" : "disabled");

            await _webhookEventPublisher.PublishAsync(
                enabled ? WebhookEventTypes.UserEnabled : WebhookEventTypes.UserDisabled,
                new { userId = user.UserId, email = user.Email, enabled, reason, actingUserId });

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(user));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting status for user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while updating the user");
        }
    }

    public async Task<ServiceResult<bool>> DeleteUserAsync(string userId, string actingUserId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
                return ServiceResult<bool>.BadRequest("User id is required");

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            var selfDelete = RejectIfSelfDelete(userId, actingUserId);
            if (selfDelete != null)
                return selfDelete;

            var lastSysAdmin = await RejectIfLastEnabledSysAdminAsync(user);
            if (lastSysAdmin != null)
                return lastSysAdmin;

            var deleted = await _userRepository.DeleteUser(userId, tenantId: null);
            if (!deleted)
                return ServiceResult<bool>.InternalServerError("Failed to delete user");

            await InvalidateCachesAsync(user);
            _logger.LogInformation(
                "Global user {UserId} deleted by {ActingUserId}",
                LogSanitizer.Sanitize(userId),
                LogSanitizer.Sanitize(actingUserId));

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.UserDeleted,
                new { userId = user.UserId, email = user.Email, name = user.Name, actingUserId });

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting global user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the user");
        }
    }

    private ServiceResult<bool>? RejectIfSelfDelete(string userId, string actingUserId)
    {
        if (string.IsNullOrEmpty(actingUserId) ||
            !string.Equals(actingUserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        _logger.LogWarning(
            "User {UserId} attempted to delete their own account",
            LogSanitizer.Sanitize(userId));
        return ServiceResult<bool>.Forbidden("Cannot delete your own account");
    }

    private async Task<ServiceResult<bool>?> RejectIfLastEnabledSysAdminAsync(User user)
    {
        if (!user.IsSysAdmin)
            return null;

        var sysAdmins = await _userRepository.GetSystemAdminAsync();
        var remainingEnabled = sysAdmins.Count(admin =>
            !string.Equals(admin.UserId, user.UserId, StringComparison.Ordinal) && !admin.IsLockedOut);

        if (remainingEnabled > 0)
            return null;

        _logger.LogWarning(
            "Refusing to delete last enabled SysAdmin {UserId}",
            LogSanitizer.Sanitize(user.UserId));
        return ServiceResult<bool>.BadRequest("Cannot delete the last enabled system administrator");
    }

    /// <summary>
    /// Records the case where enabling this account takes SysAdmin away from another one.
    ///
    /// An address only authenticates as a system administrator when every enabled account holding
    /// it has the role, so enabling one that does not withdraws it — from a different account than
    /// the one being enabled, on credentials that name only an email. Granting this account the
    /// same role puts it back.
    /// </summary>
    private async Task WarnIfEnablingDemotesASysAdminAsync(User user)
    {
        if (user.IsSysAdmin || string.IsNullOrWhiteSpace(user.Email))
            return;

        var owners = await _userRepository.GetAllByUserEmailAsync(user.Email);
        var affected = owners
            .Where(owner => owner.IsSysAdmin && owner.UserId != user.UserId)
            .Select(owner => owner.UserId)
            .ToList();

        if (affected.Count == 0)
            return;

        _logger.LogError(
            "Enabled {UserId}, which holds the same email as system administrator(s) {Others} " +
            "without holding that role. Until it is granted the same role, that address no longer " +
            "authenticates as a system administrator on credentials naming only an email",
            LogSanitizer.Sanitize(user.UserId),
            LogSanitizer.Sanitize(string.Join(", ", affected)));
    }

    private static GlobalUserSummary ToSummary(User user)
    {
        return new GlobalUserSummary
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            IsSysAdmin = user.IsSysAdmin,
            IsEnabled = !user.IsLockedOut,
            TenantCount = user.TenantRoles.Count,
            ProviderAuthority = user.ProviderAuthority,
            IsLockedOut = user.IsLockedOut,
            LockedOutReason = user.LockedOutReason,
            LockedOutAt = user.LockedOutAt,
            LockedOutBy = user.LockedOutBy,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
        };
    }

    private async Task<GlobalUserDetail> ToDetailAsync(User user)
    {
        var memberships = new List<GlobalUserMembership>(user.TenantRoles.Count);
        foreach (var tr in user.TenantRoles)
        {
            var tenant = await _tenantCacheService.GetByTenantIdAsync(tr.Tenant);
            memberships.Add(new GlobalUserMembership
            {
                TenantId = tr.Tenant,
                TenantName = tenant?.Name ?? tr.Tenant,
                Roles = tr.Roles,
                IsApproved = tr.IsApproved,
            });
        }

        return new GlobalUserDetail
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            IsSysAdmin = user.IsSysAdmin,
            IsEnabled = !user.IsLockedOut,
            DisabledReason = user.IsLockedOut ? user.LockedOutReason : null,
            ProviderAuthority = user.ProviderAuthority,
            IsLockedOut = user.IsLockedOut,
            LockedOutReason = user.LockedOutReason,
            LockedOutAt = user.LockedOutAt,
            LockedOutBy = user.LockedOutBy,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Memberships = memberships,
        };
    }

    private async Task InvalidateCachesAsync(User user)
    {
        await _authorizationInvalidator.InvalidateAsync(user);
    }
}
