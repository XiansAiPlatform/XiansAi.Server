using Features.WebApi.Auth;

namespace Features.AdminApi.Auth;

/// <summary>
/// Authentication and authorization extensions for AdminApi endpoints.
/// AdminApi requires admin roles (SysAdmin or TenantAdmin).
/// </summary>
public static class AdminApiAuthExtensions
{
    /// <summary>
    /// Requires valid tenant admin or system admin for AdminApi endpoints.
    /// </summary>
    public static T RequiresAdminApiAuth<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder
            .RequiresValidTenant()
            .RequiresValidTenantAdmin(); // Requires TenantAdmin or SysAdmin
    }

    /// <summary>
    /// Requires system admin for AdminApi endpoints that need system-level access.
    /// </summary>
    public static T RequiresSysAdminApiAuth<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder
            .RequiresValidTenant()
            .RequiresValidSysAdmin();
    }
}

