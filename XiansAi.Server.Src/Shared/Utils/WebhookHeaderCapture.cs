using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Shared.Utils;

/// <summary>
/// Captures inbound HTTP headers for webhook metadata forwarding.
/// </summary>
public static class WebhookHeaderCapture
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Set-Cookie",
        "X-Api-Key"
    };

    /// <summary>
    /// Returns captured headers, or null if none captured.
    /// </summary>
    public static Dictionary<string, string>? Capture(
        IHeaderDictionary requestHeaders,
        bool excludeSensitive = false)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var header in requestHeaders)
        {
            if (excludeSensitive && IsSensitive(header.Key))
                continue;

            var value = GetFirstNonEmpty(header.Value);
            if (value is null)
                continue;

            result[header.Key] = value;
        }

        return result.Count > 0 ? result : null;
    }

    private static string? GetFirstNonEmpty(StringValues values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrEmpty(v))
                return v;
        }

        return null;
    }

    private static bool IsSensitive(string name) =>
        SensitiveHeaders.Contains(name) ||
        name.Equals("Auth", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("-Auth", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("api_key", StringComparison.OrdinalIgnoreCase);
}
