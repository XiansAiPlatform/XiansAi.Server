using Shared.Utils.Services;

namespace Shared.Auth;

/// <summary>
/// Enforces tenant scope for Secret Vault operations based on caller role.
/// SysAdmin: may perform CRUD on any tenant. TenantAdmin: only on the tenant from their certificate/context.
/// </summary>
public static class SecretVaultScopeEnforcement
{
    /// <summary>
    /// Resolves the effective scope for create/list/fetch operations.
    /// SysAdmin: uses request scope as-is. TenantAdmin (or no SysAdmin): effective tenant is forced to context tenant;
    /// if request specifies a different tenantId, returns a Forbidden result.
    /// </summary>
    /// <param name="tenantContext">Current auth context (certificate or admin user).</param>
    /// <param name="requestTenantId">TenantId from request body/query.</param>
    /// <param name="requestAgentId">AgentId from request.</param>
    /// <param name="requestUserId">UserId from request.</param>
    /// <param name="requestActivationName">ActivationName from request.</param>
    /// <param name="effectiveTenantId">Resolved tenant ID to use.</param>
    /// <param name="effectiveAgentId">Resolved agent ID (unchanged from request).</param>
    /// <param name="effectiveUserId">Resolved user ID (unchanged from request).</param>
    /// <param name="effectiveActivationName">Resolved activation name (unchanged from request).</param>
    /// <returns>True if scope is allowed; false with a Forbidden ServiceResult to return.</returns>
    public static bool TryResolveScope(
        ITenantContext tenantContext,
        string? requestTenantId,
        string? requestAgentId,
        string? requestUserId,
        string? requestActivationName,
        out string? effectiveTenantId,
        out string? effectiveAgentId,
        out string? effectiveUserId,
        out string? effectiveActivationName,
        out ServiceResult<object>? forbiddenResult)
    {
        effectiveTenantId = requestTenantId;
        effectiveAgentId = requestAgentId;
        effectiveUserId = requestUserId;
        effectiveActivationName = requestActivationName;
        forbiddenResult = null;

        var isSysAdmin = tenantContext.UserRoles != null &&
                         tenantContext.UserRoles.Contains(SystemRoles.SysAdmin, StringComparer.OrdinalIgnoreCase);

        if (isSysAdmin)
            return true;

        // TenantAdmin or agent certificate without SysAdmin: restrict to context tenant only
        var contextTenantId = tenantContext.TenantId;
        if (string.IsNullOrEmpty(contextTenantId))
        {
            forbiddenResult = ServiceResult<object>.Forbidden(
                "Tenant scope is required. Certificate or user context has no tenant.");
            return false;
        }

        // Request must not specify a different tenant
        if (!string.IsNullOrEmpty(requestTenantId) &&
            !string.Equals(requestTenantId, contextTenantId, StringComparison.Ordinal))
        {
            forbiddenResult = ServiceResult<object>.Forbidden(
                "Access denied. Secret Vault operations are only allowed within your tenant.");
            return false;
        }

        effectiveTenantId = contextTenantId;
        return true;
    }

    /// <summary>
    /// Checks whether the caller may access a secret that belongs to the given tenant.
    /// SysAdmin: yes. TenantAdmin: only if secretTenantId equals the context tenant (or is null for cross-tenant; we treat null as SysAdmin-only).
    /// </summary>
    /// <param name="tenantContext">Current auth context.</param>
    /// <param name="secretTenantId">The tenant ID of the secret (from GetById).</param>
    /// <returns>True if access is allowed; otherwise false.</returns>
    public static bool CanAccessSecretTenant(ITenantContext tenantContext, string? secretTenantId)
    {
        var isSysAdmin = tenantContext.UserRoles != null &&
                         tenantContext.UserRoles.Contains(SystemRoles.SysAdmin, StringComparer.OrdinalIgnoreCase);

        if (isSysAdmin)
            return true;

        var contextTenantId = tenantContext.TenantId;
        if (string.IsNullOrEmpty(contextTenantId))
            return false;

        // TenantAdmin may only access secrets in their tenant. Cross-tenant secrets (null) are SysAdmin-only.
        return string.Equals(secretTenantId, contextTenantId, StringComparison.Ordinal);
    }
}
