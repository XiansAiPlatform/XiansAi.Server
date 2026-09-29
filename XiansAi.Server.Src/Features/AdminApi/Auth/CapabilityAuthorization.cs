using Features.AdminApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Auth;
using Shared.Utils;

namespace Features.AdminApi.Auth;

/// <summary>Declares the <see cref="CapabilityActions"/> action a route performs.</summary>
public sealed class RequireCapabilityMetadata
{
    public string Action { get; }

    public RequireCapabilityMetadata(string action) => Action = action;
}

/// <summary>
/// Marks a route group as capability-enforced, which makes a route that declares no action a denial
/// instead of a pass-through.
/// </summary>
public sealed class CapabilityEnforcedMetadata
{
    public static readonly CapabilityEnforcedMetadata Instance = new();

    private CapabilityEnforcedMetadata() { }
}

public static class CapabilityEndpointExtensions
{
    /// <summary>Declares a route's action. Applied per route inside an <see cref="EnforceCapabilities{TBuilder}"/> group.</summary>
    public static TBuilder RequireCapability<TBuilder>(this TBuilder builder, string action)
        where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(new RequireCapabilityMetadata(action));

    /// <summary>
    /// Turns on capability enforcement for a route group, adding the filter and the default-deny
    /// marker together so a group cannot end up with one but not the other.
    ///
    /// Per group rather than once on the whole AdminApi group: ASP.NET Core runs ancestor-group
    /// filters first, so a global registration would run the capability check before
    /// <see cref="TenantRouteScopeFilter"/> and a cross-tenant probe would get "capability denied"
    /// instead of the accurate "tenant scope mismatch".
    /// </summary>
    public static TBuilder EnforceCapabilities<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder
            .WithMetadata(CapabilityEnforcedMetadata.Instance)
            .AddEndpointFilter<TBuilder, CapabilityMatrixFilter>();
}

/// <summary>
/// Authorizes a request against the capability matrix: the action a route declares is checked against
/// the roles that action's stored rule, or its code default, allows.
/// </summary>
public sealed class CapabilityMatrixFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var endpoint = httpContext.GetEndpoint();

        var requirement = endpoint?.Metadata.GetMetadata<RequireCapabilityMetadata>();
        var enforced = endpoint?.Metadata.GetMetadata<CapabilityEnforcedMetadata>() != null;

        // Not capability-guarded in either sense: nothing resolved, nothing read.
        if (requirement == null && !enforced)
        {
            return await next(context);
        }

        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (AdminTenantScopeGuard.IsSysAdmin(tenantContext))
        {
            return await next(context);
        }

        if (requirement == null)
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("AdminCapabilityMatrix")
                .LogError(
                    "Route {Route} is in a capability-enforced group but declares no capability action; denying. " +
                    "Add .RequireCapability(CapabilityActions....) to this route.",
                    LogSanitizer.Sanitize(endpoint?.DisplayName));
            return AdminTenantScopeGuard.SysAdminRequired();
        }

        var allowedRoles = await httpContext.RequestServices
            .GetRequiredService<ICapabilityMatrixService>()
            .GetAllowedRolesAsync(requirement.Action);

        if (tenantContext.UserRoles?.Any(allowedRoles.Contains) == true)
        {
            return await next(context);
        }

        httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AdminCapabilityMatrix")
            .LogWarning(
                "Capability denied: {Action} allows [{AllowedRoles}] but caller {UserId} holds [{UserRoles}]",
                LogSanitizer.Sanitize(requirement.Action),
                LogSanitizer.Sanitize(string.Join(", ", allowedRoles)),
                LogSanitizer.Sanitize(tenantContext.LoggedInUser),
                LogSanitizer.Sanitize(string.Join(", ", tenantContext.UserRoles ?? [])));

        return AdminTenantScopeGuard.CapabilityDenied(requirement.Action);
    }
}
