using System.Text.Json;
using Temporalio.Converters;

namespace Shared.Utils.Temporal;

/// <summary>
/// Reads Temporal memo values as text. Temporal stores memo values as JSON, which escapes
/// non-ASCII characters (å is stored as å), so the payload must be decoded before use.
/// </summary>
public static class TemporalMemoDecoder
{
    /// <summary>
    /// Gets the decoded text of a memo entry, or null when the key is absent.
    /// </summary>
    public static string? GetString(IReadOnlyDictionary<string, IEncodedRawValue> memo, string key)
    {
        return memo.TryGetValue(key, out var value) ? Decode(value) : null;
    }

    /// <summary>
    /// Decodes a memo value. JSON strings are unescaped and JSON null becomes null.
    /// Other JSON values (for example booleans) and non-JSON payloads are returned as their raw
    /// text without double quotes.
    /// </summary>
    public static string? Decode(IEncodedRawValue? value)
    {
        var raw = value?.Payload?.Data?.ToStringUtf8();
        if (raw == null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Null => null,
                _ => StripQuotes(raw)
            };
        }
        catch (JsonException)
        {
            return StripQuotes(raw);
        }
    }

    private static string StripQuotes(string raw) => raw.Replace("\"", "");
}
