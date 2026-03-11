using Shared.Auth;
using Shared.Data.Models;
using Shared.Exceptions;
using Shared.Services;

namespace Features.AdminApi.Auth;

/// <summary>
/// Result of resolving admin roles and tenant context.
/// </summary>
public sealed record AdminRoleTenantResolutionResult(
    bool Success,
    string? FinalTenantId,
    string[]? UserRoles,
    string? ErrorMessage);

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
    Task<AdminRoleTenantResolutionResult> ResolveAsync(
        string userId,
        ApiKey apiKey,
        string tenantIdFromRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of admin role and tenant resolution logic.
/// </summary>
public sealed class AdminRoleTenantResolver : IAdminRoleTenantResolver
{
    private readonly IRoleCacheService _roleCacheService;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly ILogger<AdminRoleTenantResolver> _logger;

    public AdminRoleTenantResolver(
        IRoleCacheService roleCacheService,
        ITenantCacheService tenantCacheService,
        ILogger<AdminRoleTenantResolver> logger)
    {
        _roleCacheService = roleCacheService;
        _tenantCacheService = tenantCacheService;
        _logger = logger;
    }

    public async Task<AdminRoleTenantResolutionResult> ResolveAsync(
        string userId,
        ApiKey apiKey,
        string tenantIdFromRequest,
        CancellationToken cancellationToken = default)
    {
        var userRoles = await _roleCacheService.GetUserRolesAsync(userId, apiKey.TenantId);

        var hasSysAdmin = userRoles.Contains(SystemRoles.SysAdmin);
        var hasTenantAdmin = userRoles.Contains(SystemRoles.TenantAdmin);

        if (!hasSysAdmin && !hasTenantAdmin)
        {
            _logger.LogWarning(
                "User {UserId} does not have SysAdmin or TenantAdmin role. Roles: {Roles}",
                userId, string.Join(", ", userRoles));
            return new AdminRoleTenantResolutionResult(
                Success: false,
                null, null,
                "User does not have required admin role");
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
                        userId, tenantIdFromRequest);
                    throw new TenantNotFoundException(tenantIdFromRequest);
                }
                finalTenantId = tenantIdFromRequest;
                _logger.LogDebug(
                    "SysAdmin user {UserId} using provided tenantId: {TenantId}",
                    userId, finalTenantId);
            }
            else
            {
                finalTenantId = apiKey.TenantId;
                _logger.LogDebug(
                    "SysAdmin user {UserId} using API key tenantId: {TenantId}",
                    userId, finalTenantId);
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
                        userId, tenantIdFromRequest, apiKey.TenantId);
                    return new AdminRoleTenantResolutionResult(
                        Success: false,
                        null, null,
                        "Tenant ID does not match API key tenant");
                }
                finalTenantId = tenantIdFromRequest;
                _logger.LogDebug(
                    "TenantAdmin user {UserId} validated tenantId: {TenantId}",
                    userId, finalTenantId);
            }
            else
            {
                finalTenantId = apiKey.TenantId;
                _logger.LogDebug(
                    "TenantAdmin user {UserId} using API key tenantId: {TenantId}",
                    userId, finalTenantId);
            }
        }

        return new AdminRoleTenantResolutionResult(
            Success: true,
            finalTenantId,
            userRoles.ToArray(),
            null);
    }
}
