using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Shared.Utils;

/// <summary>
/// Captures inbound HTTP headers for webhook metadata forwarding.
/// </summary>
public static class WebhookHeaderCapture
{
    /// <summary>
    /// Returns captured headers, or null if none captured.
    /// </summary>
    public static Dictionary<string, string>? Capture(IHeaderDictionary requestHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var header in requestHeaders)
        {
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
}
