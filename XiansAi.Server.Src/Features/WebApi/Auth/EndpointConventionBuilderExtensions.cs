
namespace Features.WebApi.Auth;

/// <summary>
/// Extension methods for endpoint convention builders to apply authentication and authorization.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// Requires valid tenant for the endpoint with full tenant configuration validation.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with valid tenant authentication applied.</returns>
    public static T RequiresValidTenant<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTenantAuth");
    }

    public static T RequiresValidTenantAdmin<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTenantAdmin");
    }

    public static T RequiresValidSysAdmin<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireSysAdmin");
    }

    /// <summary>
    /// Requires valid tenant for the endpoint but does not validate tenant configuration.
    /// Use this for endpoints that need tenant context but don't need tenant-specific configuration.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with tenant authentication (without config validation) applied.</returns>
    public static T RequiresValidTenantWithoutConfig<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTenantAuthWithoutConfig");
    }

    /// <summary>
    /// Requires valid token for the endpoint without tenant validation.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with token authentication applied.</returns>
    public static T RequiresToken<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTokenAuth");
    }
}