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
}

public class GlobalUserAdminService : IGlobalUserAdminService
{
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly IUserRepository _userRepository;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly IRoleCacheService _roleCacheService;
    private readonly ITokenValidationCache _tokenCache;
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly ILogger<GlobalUserAdminService> _logger;

    public GlobalUserAdminService(
        IUserRepository userRepository,
        ITenantCacheService tenantCacheService,
        IRoleCacheService roleCacheService,
        ITokenValidationCache tokenCache,
        IWebhookEventPublisher webhookEventPublisher,
        ILogger<GlobalUserAdminService> logger)
    {
        _userRepository = userRepository;
        _tenantCacheService = tenantCacheService;
        _roleCacheService = roleCacheService;
        _tokenCache = tokenCache;
        _webhookEventPublisher = webhookEventPublisher;
        _logger = logger;
    }

    public async Task<ServiceResult<GlobalUserListResult>> ListUsersAsync(UserFilter filter)
    {
        try
        {
            var normalized = new UserFilter
            {
                Page = filter.Page > 0 ? filter.Page : 1,
                PageSize = Math.Min(filter.PageSize > 0 ? filter.PageSize : DefaultPageSize, MaxPageSize),
                Type = UserTypeFilter.ALL,
                Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(),
                IsSysAdmin = filter.IsSysAdmin,
                IsEnabled = filter.IsEnabled,
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
                if (ok) user.IsLockedOut = false;
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
            Memberships = memberships,
        };
    }

    private async Task InvalidateCachesAsync(User user)
    {
        foreach (var tr in user.TenantRoles)
            _roleCacheService.InvalidateUserRoles(user.UserId, tr.Tenant);
        await _tokenCache.InvalidateUserTokens(user.UserId);
    }
}
