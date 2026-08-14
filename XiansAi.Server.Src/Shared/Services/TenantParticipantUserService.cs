using System.Text.Json.Serialization;
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
    /// <summary>All roles the user holds in this tenant.</summary>
    [JsonPropertyName("roles")]
    public List<string> Roles { get; init; } = new();
    /// <summary>True only when the tenant role is approved and the user is not locked out.</summary>
    [JsonPropertyName("isApproved")]
    public required bool IsApproved { get; init; }
    [JsonPropertyName("isSysAdmin")]
    public bool IsSysAdmin { get; init; }
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
    Task<ServiceResult<PagedParticipantResult>> ListAsync(string tenantId, int page, int pageSize, string? search, string? role = null);
    Task<ServiceResult<TenantParticipantUser>> GetAsync(string tenantId, string userId);
    /// <summary>
    /// Grants <paramref name="role"/> in the tenant to the account named by <paramref name="userId"/>,
    /// or, when no user id is given, to the single account holding <paramref name="email"/> —
    /// creating one from that address and <paramref name="name"/> if none does.
    ///
    /// An address held by more than one account settles nothing, and is refused; that is what
    /// <paramref name="userId"/> is for.
    /// </summary>
    Task<ServiceResult<TenantParticipantUser>> CreateAsync(
        string tenantId, string? email, string? name, string role, string? userId = null);
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
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly ILogger<TenantParticipantUserService> _logger;

    public TenantParticipantUserService(
        IUserRepository userRepository,
        IUserTenantService userTenantService,
        IRoleCacheService roleCacheService,
        ITokenValidationCache tokenCache,
        IWebhookEventPublisher webhookEventPublisher,
        ILogger<TenantParticipantUserService> logger)
    {
        _userRepository = userRepository;
        _userTenantService = userTenantService;
        _roleCacheService = roleCacheService;
        _tokenCache = tokenCache;
        _webhookEventPublisher = webhookEventPublisher;
        _logger = logger;
    }

    public async Task<ServiceResult<PagedParticipantResult>> ListAsync(string tenantId, int page, int pageSize, string? search, string? role = null)
    {
        try
        {
            string? normalizedRole = null;
            if (!string.IsNullOrWhiteSpace(role))
            {
                normalizedRole = NormalizeTenantRole(role);
                if (normalizedRole == null)
                    return ServiceResult<PagedParticipantResult>.BadRequest(
                        $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");
            }

            var filter = new UserFilter
            {
                Page = page > 0 ? page : 1,
                PageSize = pageSize > 0 ? pageSize : DefaultPageSize,
                Type = UserTypeFilter.ALL,
                Tenant = tenantId,
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                Role = normalizedRole,
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

    public async Task<ServiceResult<TenantParticipantUser>> CreateAsync(
        string tenantId, string? email, string? name, string role, string? userId = null)
    {
        var normalizedRole = NormalizeTenantRole(role);
        if (normalizedRole == null)
            return ServiceResult<TenantParticipantUser>.BadRequest(
                $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");

        // An existing account is named by its user id. An address is not an identifier here: it can
        // answer to more than one account, and the role granted may be TenantAdmin, so resolving
        // one would risk handing that role to a different account than the caller meant.
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return await AddRoleToNamedAccountAsync(userId.Trim(), tenantId, normalizedRole);
        }

        var sanitizedEmail = ValidationHelpers.SanitizeAndValidateEmail(email ?? string.Empty);
        if (sanitizedEmail == null)
            return ServiceResult<TenantParticipantUser>.BadRequest(
                "Supply either a userId for an existing account, or a valid email to create a new one");

        // An address exactly one account holds names it as surely as a user id does, so the
        // membership is added rather than the caller being sent to look up something the address
        // already settles. Refusing here would not make anything safer: an operator sent to find
        // the user id would search by the same address and arrive at the same record.
        //
        // Several accounts is the case a user id exists to settle, and the role granted here can
        // be TenantAdmin, so that one is refused rather than resolved to whichever came back first.
        var lookup = EmailAccountLookup.From(await _userRepository.GetAllByUserEmailAsync(sanitizedEmail));
        if (lookup.IsAmbiguous)
        {
            _logger.LogWarning(
                "Refusing to add {Email} to tenant {TenantId} as {Role}: it matches {Count} accounts",
                LogSanitizer.RedactEmail(sanitizedEmail), LogSanitizer.Sanitize(tenantId),
                normalizedRole, lookup.MatchCount);
            return ServiceResult<TenantParticipantUser>.Conflict(EmailAccountLookup.AmbiguousError);
        }

        if (lookup.Account != null)
            return await AddTenantToExistingUserAsync(lookup.Account, tenantId, normalizedRole);

        // Only a new account needs a name; adding an existing one takes the name already on it.
        var sanitizedName = ValidationHelpers.SanitizeString(name ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitizedName))
            return ServiceResult<TenantParticipantUser>.BadRequest(
                "Name is required to create a new account");

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

        await _webhookEventPublisher.PublishAsync(
            WebhookEventTypes.UserCreated,
            new
            {
                userId = created.UserId,
                email = created.Email,
                name = created.Name,
                tenantId,
                role = normalizedRole,
            },
            tenantId);

        return ServiceResult<TenantParticipantUser>.Success(
            ToTenantUserDto(
                created,
                normalizedRole,
                !created.IsLockedOut && (tenantRole?.IsApproved ?? true),
                tenantRole?.Roles ?? new List<string> { normalizedRole }),
            StatusCode.Created);
    }

    /// <summary>
    /// Adds the role to the account the caller named by user id, which is the only identifier that
    /// names exactly one account.
    /// </summary>
    private async Task<ServiceResult<TenantParticipantUser>> AddRoleToNamedAccountAsync(
        string userId, string tenantId, string normalizedRole)
    {
        if (userId.Contains('@'))
        {
            return ServiceResult<TenantParticipantUser>.BadRequest(
                "userId must be a user id, not an email address. Omit it and supply email and " +
                "name to create a new account.");
        }

        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            return ServiceResult<TenantParticipantUser>.NotFound($"User '{userId}' not found");
        }

        return await AddTenantToExistingUserAsync(user, tenantId, normalizedRole);
    }

    private async Task<ServiceResult<TenantParticipantUser>> AddTenantToExistingUserAsync(
        User user, string tenantId, string normalizedRole)
    {
        // A disabled account cannot sign in, and the one kind most likely to be found by address is
        // a record created disabled because it collides with a system administrator. Granting it a
        // membership would leave that grant waiting on a decision nobody made here.
        if (user.IsLockedOut)
        {
            _logger.LogWarning(
                "Refusing to add disabled account {UserId} to tenant {TenantId}",
                LogSanitizer.Sanitize(user.UserId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantParticipantUser>.Conflict(
                "This account is disabled, so it cannot be added to a tenant. Enable it first.");
        }

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

        await _webhookEventPublisher.PublishAsync(
            WebhookEventTypes.UserTenantAdded,
            new
            {
                userId = user.UserId,
                email = user.Email,
                name = user.Name,
                tenantId,
                role = normalizedRole,
            },
            tenantId);

        return ServiceResult<TenantParticipantUser>.Success(MapToTenantUser(user, tenantId)!, StatusCode.Created);
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

            var wasApproved = tr.IsApproved;
            var profileChanged = false;

            if (name != null)
            {
                var sanitized = ValidationHelpers.SanitizeString(name);
                if (string.IsNullOrWhiteSpace(sanitized))
                    return ServiceResult<TenantParticipantUser>.BadRequest("Name cannot be empty");
                user.Name = sanitized;
                profileChanged = true;
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
                profileChanged = true;
            }

            if (isApproved.HasValue)
            {
                if (isApproved.Value && user.IsLockedOut)
                    return ServiceResult<TenantParticipantUser>.Conflict("Cannot approve a user that is locked out by system administrator.");
                tr.IsApproved = isApproved.Value;
            }

            string? addedRole = null;
            if (role != null)
            {
                var normalizedRole = NormalizeTenantRole(role);
                if (normalizedRole == null)
                    return ServiceResult<TenantParticipantUser>.BadRequest(
                        $"Role must be one of: {string.Join(", ", AllowedTenantRoles)}");

                if (!tr.Roles.Contains(normalizedRole))
                {
                    tr.Roles.Add(normalizedRole);
                    addedRole = normalizedRole;
                }
            }

            var updated = await _userRepository.UpdateAsync(userId, user);
            if (!updated)
                return ServiceResult<TenantParticipantUser>.InternalServerError("Update failed");

            await InvalidateCachesAsync(userId, tenantId);

            if (profileChanged)
            {
                await _webhookEventPublisher.PublishAsync(
                    WebhookEventTypes.UserUpdated,
                    new { userId = user.UserId, email = user.Email, name = user.Name, tenantId },
                    tenantId);
            }

            if (isApproved.HasValue && isApproved.Value != wasApproved)
            {
                await _webhookEventPublisher.PublishAsync(
                    isApproved.Value ? WebhookEventTypes.UserApproved : WebhookEventTypes.UserUnapproved,
                    new { userId = user.UserId, email = user.Email, tenantId },
                    tenantId);
            }

            if (addedRole != null)
            {
                await _webhookEventPublisher.PublishAsync(
                    WebhookEventTypes.UserRoleChanged,
                    new
                    {
                        userId = user.UserId,
                        email = user.Email,
                        tenantId,
                        role = addedRole,
                        roles = tr.Roles,
                    },
                    tenantId);
            }

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

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.UserTenantRemoved,
                new { userId = user.UserId, email = user.Email, name = user.Name, tenantId },
                tenantId);

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

            var membershipRemoved = tr.Roles.Count == 0;
            if (membershipRemoved)
                user.TenantRoles.RemoveAll(t => t.Tenant == tenantId);

            var ok = await _userRepository.UpdateAsync(userId, user);
            if (!ok)
                return ServiceResult<bool>.InternalServerError("Failed to remove role");

            await InvalidateCachesAsync(userId, tenantId);

            _logger.LogInformation("Role {Role} removed from user {UserId} in tenant {TenantId}",
                normalizedRole, LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.UserRoleRemoved,
                new { userId = user.UserId, email = user.Email, tenantId, role = normalizedRole },
                tenantId);

            if (membershipRemoved)
            {
                await _webhookEventPublisher.PublishAsync(
                    WebhookEventTypes.UserTenantRemoved,
                    new { userId = user.UserId, email = user.Email, name = user.Name, tenantId },
                    tenantId);
            }

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

        return ToTenantUserDto(
            user,
            PrimaryRole(tr.Roles),
            !user.IsLockedOut && tr.IsApproved,
            tr.Roles);
    }

    private static TenantParticipantUser ToTenantUserDto(
        User user,
        string role,
        bool isApproved,
        List<string> roles)
    {
        return new TenantParticipantUser
        {
            UserId = user.UserId,
            Email = user.Email,
            Name = user.Name,
            Role = role,
            Roles = roles,
            IsApproved = isApproved,
            IsSysAdmin = user.IsSysAdmin,
            ProviderAuthority = user.ProviderAuthority,
            IsLockedOut = user.IsLockedOut,
            LockedOutReason = user.LockedOutReason,
            LockedOutAt = user.LockedOutAt,
            LockedOutBy = user.LockedOutBy,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
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
