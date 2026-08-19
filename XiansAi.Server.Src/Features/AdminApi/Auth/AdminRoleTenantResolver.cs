using Shared.Auth;
using Shared.Data.Models;
using Shared.Exceptions;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;

namespace Features.AdminApi.Auth;

/// <summary>
/// Result of resolving admin roles and tenant context.
/// <paramref name="ResolvedUserId"/> is the canonical user_id after resolving the API key owner
/// (which may be stored as a user id or as a legacy email). Null when not applicable.
/// </summary>
public sealed record AdminRoleTenantResolutionResult(
    bool Success,
    string? FinalTenantId,
    string[]? UserRoles,
    string? ErrorMessage,
    string? ResolvedUserId = null);

/// <summary>
/// Resolves user roles and target tenant for Admin API requests.
/// Centralizes logic shared between AdminEndpointAuthenticationHandler and ValidAdminEndpointAccessHandler.
/// Uses IRoleCacheService and ITenantCacheService to reduce database load.
/// </summary>
public interface IAdminRoleTenantResolver
{
    /// <summary>
    /// Resolves the final tenant ID and user roles based on API key and request context.
    /// Throws TenantNotFoundException when SysAdmin specifies a non-existent tenant.
    /// </summary>
    /// <param name="userIdOrEmail">
    /// API key owner identity: normally <see cref="User.UserId"/>, but legacy keys may store email.
    /// </param>
    /// <param name="apiKey">The authenticated API key.</param>
    /// <param name="tenantIdFromRequest">Optional tenant override from query, route, or header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminRoleTenantResolutionResult> ResolveAsync(
        string userIdOrEmail,
        ApiKey apiKey,
        string tenantIdFromRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of admin role and tenant resolution logic.
/// Access requires an explicit admin role (SysAdmin or TenantAdmin); callers without one
/// are denied. Email-domain matching is intentionally NOT used to grant access, since that
/// would let any user reach admin operations on a tenant whose domain matches their email.
/// </summary>
public sealed class AdminRoleTenantResolver : IAdminRoleTenantResolver
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleCacheService _roleCacheService;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly ILogger<AdminRoleTenantResolver> _logger;

    public AdminRoleTenantResolver(
        IUserRepository userRepository,
        IRoleCacheService roleCacheService,
        ITenantCacheService tenantCacheService,
        ILogger<AdminRoleTenantResolver> logger)
    {
        _userRepository = userRepository;
        _roleCacheService = roleCacheService;
        _tenantCacheService = tenantCacheService;
        _logger = logger;
    }

    public async Task<AdminRoleTenantResolutionResult> ResolveAsync(
        string userIdOrEmail,
        ApiKey apiKey,
        string tenantIdFromRequest,
        CancellationToken cancellationToken = default)
    {
        // Modern keys already store the canonical user id; only legacy email CreatedBy values
        // need a DB round-trip. This runs on every Admin API auth and authorization pass, so
        // skipping the user-id path avoids an uncached lookup per request.
        //
        // An address can answer to more than one account, and the key records no more than the
        // address, so the accounts are combined rather than one being picked. The roles come back
        // with the identity and are not read from the cache, which is keyed per account.
        string resolvedUserId;
        List<string> userRoles;
        if (userIdOrEmail.Contains('@'))
        {
            var identity = await _userRepository.ResolveEmailIdentityAsync(userIdOrEmail, apiKey.TenantId);
            resolvedUserId = identity?.PrimaryUserId ?? userIdOrEmail;
            userRoles = identity == null
                ? new List<string>()
                : SystemRoles.ExcludingParticipantRoles(identity.Roles);
        }
        else
        {
            resolvedUserId = userIdOrEmail;
            userRoles = await _roleCacheService.GetUserRolesAsync(resolvedUserId, apiKey.TenantId);
        }

        var hasSysAdmin = userRoles.Contains(SystemRoles.SysAdmin);
        var hasTenantAdmin = userRoles.Contains(SystemRoles.TenantAdmin);

        if (!hasSysAdmin && !hasTenantAdmin)
        {
            _logger.LogWarning(
                "User {UserId} does not have SysAdmin or TenantAdmin role. Roles: {Roles}",
                LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(string.Join(", ", userRoles)));
            return new AdminRoleTenantResolutionResult(
                Success: false,
                null, null,
                "User does not have required admin role",
                ResolvedUserId: resolvedUserId);
        }

        string finalTenantId;
        if (hasSysAdmin)
        {
            if (!string.IsNullOrEmpty(tenantIdFromRequest))
            {
                var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantIdFromRequest, cancellationToken);
                if (tenant == null)
                {
                    _logger.LogWarning(
                        "SysAdmin user {UserId} requested non-existent tenant: {TenantId}",
                        LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(tenantIdFromRequest));
                    throw new TenantNotFoundException(tenantIdFromRequest);
                }
                finalTenantId = tenantIdFromRequest;
                _logger.LogDebug(
                    "SysAdmin user {UserId} using provided tenantId: {TenantId}",
                    LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId));
            }
            else
            {
                finalTenantId = apiKey.TenantId;
                _logger.LogDebug(
                    "SysAdmin user {UserId} using API key tenantId: {TenantId}",
                    LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId));
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(tenantIdFromRequest))
            {
                if (tenantIdFromRequest != apiKey.TenantId)
                {
                    _logger.LogWarning(
                        "TenantAdmin user {UserId} provided tenantId {ProvidedTenantId} that does not match API key tenantId {ApiKeyTenantId}",
                        LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(tenantIdFromRequest), LogSanitizer.Sanitize(apiKey.TenantId));
                    return new AdminRoleTenantResolutionResult(
                        Success: false,
                        null, null,
                        "Tenant ID does not match API key tenant",
                        ResolvedUserId: resolvedUserId);
                }
                finalTenantId = tenantIdFromRequest;
                _logger.LogDebug(
                    "TenantAdmin user {UserId} validated tenantId: {TenantId}",
                    LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId));
            }
            else
            {
                finalTenantId = apiKey.TenantId;
                _logger.LogDebug(
                    "TenantAdmin user {UserId} using API key tenantId: {TenantId}",
                    LogSanitizer.RedactUserId(resolvedUserId), LogSanitizer.Sanitize(finalTenantId));
            }
        }

        return new AdminRoleTenantResolutionResult(
            Success: true,
            finalTenantId,
            userRoles.ToArray(),
            null,
            ResolvedUserId: resolvedUserId);
    }
}
