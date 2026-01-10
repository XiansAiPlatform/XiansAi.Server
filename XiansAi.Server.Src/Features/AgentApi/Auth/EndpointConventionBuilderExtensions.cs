
namespace Features.AgentApi.Auth;

/// <summary>
/// Extension methods for endpoint convention builders to apply certificate authentication.
/// </summary>
public static class EndpointConventionBuilderExtensions
{
    /// <summary>
    /// Requires certificate authentication for the endpoint.
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with certificate authentication applied.</returns>
    public static T RequiresCertificate<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireCertificate");
    }

    /// <summary>
    /// Requires system admin role for the endpoint (certificate-based auth).
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with system admin authorization applied.</returns>
    public static T RequiresValidSysAdmin<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireCertificateSysAdmin");
    }

    /// <summary>
    /// Requires tenant admin or system admin role for the endpoint (certificate-based auth).
    /// </summary>
    /// <typeparam name="T">The type of endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder with tenant admin authorization applied.</returns>
    public static T RequiresValidTenantAdmin<T>(this T builder) where T : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization("RequireCertificateTenantAdmin");
    }
}