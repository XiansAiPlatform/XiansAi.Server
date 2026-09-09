using Microsoft.AspNetCore.Http.Metadata;

namespace Shared.Auditing;

/// <summary>
/// Reads an endpoint's own <c>.WithName(...)</c> / <c>.WithSummary(...)</c> metadata back out of
/// the current request, so a handler can forward that same text into a service call (e.g. as an
/// audit action/description) without retyping it as a separate literal.
/// </summary>
public static class EndpointMetadataExtensions
{
    /// <summary>The endpoint's <c>.WithName(...)</c> value, falling back to its route <c>DisplayName</c>.</summary>
    public static string? GetEndpointName(this HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? endpoint?.DisplayName;
    }

    /// <summary>The endpoint's <c>.WithSummary(...)</c> value, or null when none was set.</summary>
    public static string? GetEndpointSummary(this HttpContext httpContext)
    {
        var endpoint = httpContext.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary;
    }
}
