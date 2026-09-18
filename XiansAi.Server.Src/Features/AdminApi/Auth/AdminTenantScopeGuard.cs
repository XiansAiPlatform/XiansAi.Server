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
    /// Standard 403 response used when the capability matrix does not grant the caller's roles the
    /// action a route declared. Names the action so an operator can go straight to the rule that
    /// produced the refusal rather than guessing which one applied.
    /// </summary>
    public static IResult CapabilityDenied(string action) =>
        Results.Json(
            new { message = $"Access denied: your roles do not permit '{action}'", action },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Standard 401 response used when a route that forwards the raw API key downstream is called
    /// by a ID-token-only caller who has none to forward.
    /// </summary>
    public static IResult ApiKeyRequired() =>
        Results.Json(
            new { message = "This operation requires an API key; the ID-token-only path is not supported here" },
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
/// Endpoint filter that rejects a ID-token-only caller.
/// Apply to route groups that forward the API key downstream (AdminMessagingEndpoints,
/// AdminHeartbeatEndpoints both forward it to agents via Temporal signals).
/// </summary>
public sealed class RequireApiKeyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();

        if (string.IsNullOrEmpty(tenantContext.Authorization))
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AdminApiKeyRequired");
            logger.LogWarning(
                "Rejected ID-token-only caller on a route that requires an API key. Caller: {UserId}",
                LogSanitizer.Sanitize(tenantContext.LoggedInUser));
            return AdminTenantScopeGuard.ApiKeyRequired();
        }

        return await next(context);
    }
}

/// <summary>
/// Endpoint metadata marking a route as operating on a fixed/global resource rather than any
/// caller-supplied tenant. Apply via <c>.WithMetadata(TenantOptionalForSysAdminMetadata.Instance)</c>
/// on a route group that never reads <see cref="ITenantContext.TenantId"/>.
/// </summary>
public sealed class TenantOptionalForSysAdminMetadata
{
    public static readonly TenantOptionalForSysAdminMetadata Instance = new();
    private TenantOptionalForSysAdminMetadata() { }
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
