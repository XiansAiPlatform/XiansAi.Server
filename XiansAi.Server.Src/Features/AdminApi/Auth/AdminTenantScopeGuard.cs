using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Auth;
using Shared.Utils;

namespace Features.AdminApi.Auth;

/// <summary>
/// Centralized tenant-scope checks for AdminApi endpoints.
///
/// Background: <see cref="AdminRoleTenantResolver"/> resolves an authoritative tenant into
/// <see cref="ITenantContext.TenantId"/> (a TenantAdmin is locked to their own tenant; a SysAdmin
/// to the tenant they explicitly targeted). However, the tenant value used during authentication is
/// taken from the request with priority query &gt; route &gt; header, while endpoint handlers bind
/// <c>tenantId</c> from the route path. A caller can therefore satisfy authentication with a query
/// parameter for their own tenant while pointing the route at a different (victim) tenant.
///
/// These helpers (and the <see cref="TenantRouteScopeFilter"/>) make the resolved
/// <see cref="ITenantContext.TenantId"/> authoritative by rejecting any request whose route tenant
/// does not match it.
/// </summary>
public static class AdminTenantScopeGuard
{
    /// <summary>Returns true when the caller holds the system administrator role.</summary>
    public static bool IsSysAdmin(ITenantContext tenantContext) =>
        tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) == true;

    /// <summary>
    /// Returns true when the route tenant matches the resolved context tenant. Both must be present.
    /// </summary>
    public static bool RouteMatchesContext(ITenantContext tenantContext, string? routeTenantId) =>
        !string.IsNullOrEmpty(routeTenantId) &&
        !string.IsNullOrEmpty(tenantContext.TenantId) &&
        string.Equals(routeTenantId, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when the workflow ID is owned by the resolved context tenant.
    /// Workflow IDs are built as <c>{tenantId}:{agent}:{workflowType}[:{activation}]</c>
    /// (see <c>WorkflowIdentifier.BuildWorkflowId</c>), so the tenant is the
    /// segment before the first colon.
    /// </summary>
    public static bool WorkflowIdBelongsToContext(ITenantContext tenantContext, string? workflowId)
    {
        if (string.IsNullOrEmpty(workflowId) || string.IsNullOrEmpty(tenantContext.TenantId))
        {
            return false;
        }

        var separatorIndex = workflowId.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var workflowTenant = workflowId.Substring(0, separatorIndex);
        return string.Equals(workflowTenant, tenantContext.TenantId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Standard 403 response used when a tenant-scope check fails.
    /// </summary>
    public static IResult TenantScopeMismatch() =>
        Results.Json(new { message = "Tenant scope mismatch" }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Standard 403 response used when a SysAdmin-only endpoint is called without that role.
    /// </summary>
    public static IResult SysAdminRequired() =>
        Results.Json(
            new { message = "Access denied: Only system administrators can perform this operation" },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Standard 401 response used when a route requires a verified acting human and the caller
    /// only presented the shared API key.
    /// </summary>
    public static IResult VerifiedActingUserRequired() =>
        Results.Json(
            new { message = "This operation requires a verified user token (X-User-Token), not just the API key" },
            statusCode: StatusCodes.Status401Unauthorized);
}

/// <summary>
/// Endpoint filter that requires the caller to hold <see cref="SystemRoles.SysAdmin"/>.
/// Apply on AdminApi route groups that must never be reachable by TenantAdmin API keys.
/// </summary>
public sealed class SysAdminOnlyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();

        if (!AdminTenantScopeGuard.IsSysAdmin(tenantContext))
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AdminSysAdminScope");
            logger.LogWarning(
                "Access denied: SysAdmin role required. Caller: {UserId}",
                LogSanitizer.Sanitize(tenantContext.LoggedInUser));
            return AdminTenantScopeGuard.SysAdminRequired();
        }

        return await next(context);
    }
}

/// <summary>
/// Endpoint filter that enforces that the <c>tenantId</c> route value matches the resolved
/// <see cref="ITenantContext.TenantId"/>. Apply to every AdminApi route group nested under
/// <c>/tenants/{tenantId}</c> to close the route-vs-query tenant mismatch (cross-tenant IDOR) vector.
/// </summary>
public sealed class TenantRouteScopeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();

        var routeTenantId = httpContext.Request.RouteValues.TryGetValue("tenantId", out var value)
            ? value?.ToString()
            : null;

        if (!AdminTenantScopeGuard.RouteMatchesContext(tenantContext, routeTenantId))
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AdminTenantScope");
            logger.LogWarning(
                "Tenant scope mismatch: route tenant {RouteTenant} does not match resolved context tenant {CtxTenant}. Caller: {UserId}",
                LogSanitizer.Sanitize(routeTenantId),
                LogSanitizer.Sanitize(tenantContext.TenantId),
                LogSanitizer.Sanitize(tenantContext.LoggedInUser));
            return AdminTenantScopeGuard.TenantScopeMismatch();
        }

        return await next(context);
    }
}

/// <summary>
/// Endpoint filter that requires a verified acting human on the request (see
/// <c>AdminEndpointAuthenticationHandler.TryApplyVerifiedActingUserAsync</c>), not just the shared
/// API key. Apply to sensitive AdminApi routes — granting/denying access, deleting resources. A caller that omits the verified token must be
/// rejected outright, not quietly fall back to the key owner's identity. Routes with a legitimate
/// non-human caller (scripts, jobs) should not get this filter.
/// </summary>
public sealed class RequireVerifiedActingUserFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();

        if (!tenantContext.ActingUserVerified)
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AdminVerifiedActingUserScope");
            logger.LogWarning(
                "Rejected request to a verified-acting-user-required route: no verified user token present. ServiceCaller: {UserId}",
                LogSanitizer.Sanitize(tenantContext.ServiceCallerUserId ?? tenantContext.LoggedInUser));
            return AdminTenantScopeGuard.VerifiedActingUserRequired();
        }

        return await next(context);
    }
}
