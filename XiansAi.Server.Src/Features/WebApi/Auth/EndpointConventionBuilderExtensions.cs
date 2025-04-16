using Microsoft.AspNetCore.Builder;

namespace Features.WebApi.Auth;

/// <summary>
/// Extension methods for endpoint convention builders to apply certificate authentication.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// Requires valid tenant for the endpoint.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with valid tenant authentication applied.</returns>
    public static T RequiresValidTenant<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTenantAuth");
    }

    /// <summary>
    /// Requires valid tenant for the endpoint.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with valid tenant authentication applied.</returns>
    public static T RequiresToken<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireTokenAuth");
    }
}