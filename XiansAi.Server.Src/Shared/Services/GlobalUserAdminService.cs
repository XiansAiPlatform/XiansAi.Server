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
    [JsonPropertyName("linkedIdentities")]
    public required List<GlobalUserLinkedIdentity> LinkedIdentities { get; init; }
}

/// <summary>
/// A provider identity attached to an account, as shown to an administrator.
/// </summary>
public class GlobalUserLinkedIdentity
{
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }
    [JsonPropertyName("authority")]
    public required string Authority { get; init; }
    [JsonPropertyName("linkedAt")]
    public required DateTime LinkedAt { get; init; }
    [JsonPropertyName("linkedBy")]
    public required string LinkedBy { get; init; }
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
    Task<ServiceResult<GlobalUserDetail>> LinkIdentityAsync(string userId, string subject, string authority, string actingUserId);
    Task<ServiceResult<GlobalUserDetail>> UnlinkIdentityAsync(string userId, string subject, string authority);
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

    /// <summary>
    /// Attaches a provider identity to an existing account, so that signing in with it acts as that
    /// account rather than provisioning a second one.
    ///
    /// This is the only way the two are ever joined. Sign-in refuses to merge on its own, because a
    /// token proves only what its provider asserts about the holder, and the account being joined
    /// may carry far more access than they have. Asserting that the two identities are the same
    /// person is a judgement, and it is recorded against the administrator who made it.
    /// </summary>
    public async Task<ServiceResult<GlobalUserDetail>> LinkIdentityAsync(
        string userId, string subject, string authority, string actingUserId)
    {
        try
        {
            var sanitizedSubject = ValidationHelpers.SanitizeString(subject);
            if (string.IsNullOrWhiteSpace(sanitizedSubject))
                return ServiceResult<GlobalUserDetail>.BadRequest("Subject is required");

            var normalizedAuthority = LinkedIdentityKey.NormalizeAuthority(authority);
            if (!IsUsableAuthority(normalizedAuthority))
                return ServiceResult<GlobalUserDetail>.BadRequest(
                    "Authority must be the absolute https URL of the identity provider");

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            // A subject that already owns an account is never looked up as a link, because sign-in
            // matches its own id first. Accepting one would store a link that can never resolve.
            var subjectOwnsAccount = await _userRepository.GetByUserIdAsync(sanitizedSubject);
            if (subjectOwnsAccount != null)
            {
                return ServiceResult<GlobalUserDetail>.Conflict(
                    "That subject is already an account of its own and cannot be linked");
            }

            var outcome = await _userRepository.AddLinkedIdentityAsync(userId, new LinkedIdentity
            {
                Subject = sanitizedSubject,
                Authority = normalizedAuthority,
                LinkedBy = actingUserId,
                LinkedAt = DateTime.UtcNow,
            });

            if (outcome == LinkIdentityOutcome.TakenByAnotherUser)
            {
                return ServiceResult<GlobalUserDetail>.Conflict(
                    "That identity is already linked to a different account");
            }

            if (outcome == LinkIdentityOutcome.Added)
            {
                _logger.LogInformation(
                    "Administrator {ActingUserId} linked a {Authority} identity to user {UserId}",
                    LogSanitizer.RedactUserId(actingUserId), LogSanitizer.Sanitize(normalizedAuthority),
                    LogSanitizer.RedactUserId(userId));
            }

            // Re-read so the response reflects the stored links rather than the pre-update copy.
            var updated = await _userRepository.GetByUserIdAsync(userId) ?? user;
            await InvalidateCachesAsync(updated);

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.UserUpdated,
                new { userId = updated.UserId, email = updated.Email, name = updated.Name });

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(updated));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error linking identity to user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while linking the identity");
        }
    }

    /// <summary>
    /// Detaches a provider identity, which stops it resolving to this account on the next sign-in.
    /// </summary>
    public async Task<ServiceResult<GlobalUserDetail>> UnlinkIdentityAsync(
        string userId, string subject, string authority)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(subject))
                return ServiceResult<GlobalUserDetail>.BadRequest("Subject is required");

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<GlobalUserDetail>.NotFound("User not found");

            var removed = await _userRepository.RemoveLinkedIdentityAsync(userId, subject, authority);
            if (!removed)
                return ServiceResult<GlobalUserDetail>.NotFound("That identity is not linked to this user");

            _logger.LogInformation("Unlinked a {Authority} identity from user {UserId}",
                LogSanitizer.Sanitize(LinkedIdentityKey.NormalizeAuthority(authority)),
                LogSanitizer.RedactUserId(userId));

            var updated = await _userRepository.GetByUserIdAsync(userId) ?? user;
            await InvalidateCachesAsync(updated);

            return ServiceResult<GlobalUserDetail>.Success(await ToDetailAsync(updated));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlinking identity from user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<GlobalUserDetail>.InternalServerError("An error occurred while unlinking the identity");
        }
    }

    /// <summary>
    /// Whether an authority is one a token could actually have been validated against. Anything a
    /// sign-in can never produce is rejected at entry rather than stored as a link that never matches.
    /// </summary>
    private static bool IsUsableAuthority(string authority)
    {
        return Uri.TryCreate(authority, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
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

        var linkedIdentities = (user.LinkedIdentities ?? new List<LinkedIdentity>())
            .Select(li => new GlobalUserLinkedIdentity
            {
                Subject = li.Subject,
                Authority = li.Authority,
                LinkedAt = li.LinkedAt,
                LinkedBy = li.LinkedBy,
            })
            .ToList();

        return new GlobalUserDetail
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            IsSysAdmin = user.IsSysAdmin,
            IsEnabled = !user.IsLockedOut,
            Memberships = memberships,
            LinkedIdentities = linkedIdentities,
        };
    }

    private async Task InvalidateCachesAsync(User user)
    {
        foreach (var tr in user.TenantRoles)
            _roleCacheService.InvalidateUserRoles(user.UserId, tr.Tenant);
        await _tokenCache.InvalidateUserTokens(user.UserId);
    }
}
