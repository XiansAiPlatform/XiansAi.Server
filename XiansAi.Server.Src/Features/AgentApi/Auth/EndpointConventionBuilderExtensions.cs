
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
}