namespace Shared.Utils;

/// <summary>
/// Extension methods for reading standard HTTP semantics from an incoming request.
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>
    /// Returns <c>true</c> when the request carries the standard <c>Cache-Control: no-cache</c>
    /// directive, signalling that the caller wants a fresh response that bypasses any server-side cache.
    /// Parsing is delegated to ASP.NET's typed-header support, so casing and multiple values are handled.
    /// </summary>
    public static bool IsNoCacheRequested(this HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.GetTypedHeaders().CacheControl?.NoCache ?? false;
    }
}
