using Microsoft.AspNetCore.RateLimiting;

namespace Features.Shared.Configuration;

/// <summary>
/// Extension methods for applying rate limiting to endpoint groups and routes.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Applies global rate limiting policy to an endpoint.
    /// </summary>
    public static TBuilder WithGlobalRateLimit<TBuilder>(this TBuilder builder) 
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireRateLimiting(RateLimitingConfiguration.GlobalPolicy);
    }

    /// <summary>
    /// Applies authentication rate limiting policy to an endpoint (stricter limits for auth endpoints).
    /// </summary>
    public static TBuilder WithAuthenticationRateLimit<TBuilder>(this TBuilder builder) 
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireRateLimiting(RateLimitingConfiguration.AuthenticationPolicy);
    }

    /// <summary>
    /// Applies public API rate limiting policy to an endpoint.
    /// </summary>
    public static TBuilder WithPublicApiRateLimit<TBuilder>(this TBuilder builder) 
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireRateLimiting(RateLimitingConfiguration.PublicApiPolicy);
    }

    /// <summary>
    /// Applies agent/user API rate limiting policy to an endpoint.
    /// </summary>
    public static TBuilder WithAgentUserApiRateLimit<TBuilder>(this TBuilder builder) 
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireRateLimiting(RateLimitingConfiguration.AgentUserApiPolicy);
    }

    /// <summary>
    /// Disables rate limiting for an endpoint.
    /// Use sparingly and only for endpoints that have other protection mechanisms.
    /// </summary>
    public static TBuilder DisableRateLimiting<TBuilder>(this TBuilder builder) 
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.DisableRateLimiting();
    }

    /// <summary>
    /// Applies global rate limiting to an endpoint group.
    /// </summary>
    public static RouteGroupBuilder WithGlobalRateLimit(this RouteGroupBuilder group)
    {
        return group.RequireRateLimiting(RateLimitingConfiguration.GlobalPolicy);
    }

    /// <summary>
    /// Applies authentication rate limiting to an endpoint group (stricter limits for auth endpoints).
    /// </summary>
    public static RouteGroupBuilder WithAuthenticationRateLimit(this RouteGroupBuilder group)
    {
        return group.RequireRateLimiting(RateLimitingConfiguration.AuthenticationPolicy);
    }

    /// <summary>
    /// Applies public API rate limiting to an endpoint group.
    /// </summary>
    public static RouteGroupBuilder WithPublicApiRateLimit(this RouteGroupBuilder group)
    {
        return group.RequireRateLimiting(RateLimitingConfiguration.PublicApiPolicy);
    }

    /// <summary>
    /// Applies agent/user API rate limiting to an endpoint group.
    /// </summary>
    public static RouteGroupBuilder WithAgentUserApiRateLimit(this RouteGroupBuilder group)
    {
        return group.RequireRateLimiting(RateLimitingConfiguration.AgentUserApiPolicy);
    }
}

