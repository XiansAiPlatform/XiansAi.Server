using System.Security.Cryptography;
using System.Text;

namespace Shared.Services.Privacy.Export;

/// <summary>
/// Rules for file paths inside a privacy export, so the ZIP extracts safely on any operating system.
/// </summary>
/// <remarks>
/// Forward slashes only, no empty segments, at most 255 characters. Each segment uses only letters, digits,
/// <c>.</c>, <c>_</c> and <c>-</c>, is at most 100 characters, is not all dots, and is not a Windows device name
/// such as <c>CON</c>.
/// </remarks>
public static class ExportEntryPath
{
    public const int MaxSegmentLength = 100;
    public const int MaxPathLength = 255;

    // A dash plus 8 hex characters, added by ToSegment.
    private const int HashSuffixLength = 9;

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Throws if the path breaks a rule. The message names the rule, not the path.</summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    public static void Validate(string path)
    {
        if (!TryValidate(path, out var reason))
        {
            throw new ArgumentException($"Invalid export entry path: {reason}.", nameof(path));
        }
    }

    /// <summary>Returns false and the broken rule if the path is invalid.</summary>
    public static bool TryValidate(string? path, out string reason)
    {
        if (string.IsNullOrEmpty(path))
        {
            reason = "the path is empty";
            return false;
        }

        if (path.Length > MaxPathLength)
        {
            reason = $"the path is longer than {MaxPathLength} characters";
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (!TryValidateSegment(segment, out reason))
            {
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>True if the value is a valid file or folder name.</summary>
    public static bool IsValidSegment(string? segment) => TryValidateSegment(segment, out _);

    /// <summary>
    /// Turns any value, such as a workflow ID with <c>:</c>, into a valid name. Valid values are returned unchanged.
    /// Otherwise invalid characters become <c>_</c> and a short hash is added, so two values never get the same name.
    /// </summary>
    public static string ToSegment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (IsValidSegment(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(IsAllowed(character) ? character : '_');
        }

        var cleaned = builder.ToString();
        if (cleaned.Length > MaxSegmentLength - HashSuffixLength)
        {
            cleaned = cleaned[..(MaxSegmentLength - HashSuffixLength)];
        }

        if (cleaned.Length == 0)
        {
            cleaned = "_";
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
        return $"{cleaned}-{hash}";
    }

    private static bool TryValidateSegment(string? segment, out string reason)
    {
        if (string.IsNullOrEmpty(segment))
        {
            reason = "a segment is empty (leading, trailing or double slash)";
            return false;
        }

        if (segment.Length > MaxSegmentLength)
        {
            reason = $"a segment is longer than {MaxSegmentLength} characters";
            return false;
        }

        foreach (var character in segment)
        {
            if (!IsAllowed(character))
            {
                reason = "a segment contains a character other than letters, digits, '.', '_' and '-'";
                return false;
            }
        }

        if (segment.All(character => character == '.'))
        {
            reason = "a segment is made only of dots";
            return false;
        }

        var dot = segment.IndexOf('.');
        var stem = dot < 0 ? segment : segment[..dot];
        if (DeviceNames.Contains(stem))
        {
            reason = "a segment is a reserved device name";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsAllowed(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
}
