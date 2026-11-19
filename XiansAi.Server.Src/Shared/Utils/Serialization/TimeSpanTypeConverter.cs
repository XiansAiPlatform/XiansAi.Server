using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Shared.Utils.Serialization;

public partial class TimeSpanTypeConverter : IYamlTypeConverter
{
    private static readonly Regex TimeSpanRegex = TimeSpanFormatRegex();
    private static readonly Regex FullFormatRegex = FullDurationFormatRegex();
    
    public bool Accepts(Type type)
    {
        return type == typeof(TimeSpan) || type == typeof(TimeSpan?);
    }

    /// <summary>
    /// Parses a duration without throwing.
    /// Rejects blank or malformed input and anything above int.MaxValue seconds.
    /// </summary>
    public static bool TryParse(string? value, out TimeSpan result)
    {
        result = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(value) || !FullFormatRegex.IsMatch(value))
        {
            return false;
        }

        double totalSeconds = 0;
        foreach (Match match in TimeSpanRegex.Matches(value))
        {
            if (!int.TryParse(match.Groups[1].Value, out var number) ||
                !TryGetUnitSeconds(match.Groups[2].Value, out var unitSeconds))
            {
                return false;
            }

            totalSeconds += number * unitSeconds;
        }

        if (totalSeconds > int.MaxValue)
        {
            return false;
        }

        result = TimeSpan.FromSeconds(totalSeconds);
        return true;
    }

    private static bool TryGetUnitSeconds(string unit, out double seconds)
    {
        seconds = unit switch
        {
            "s" => 1d,
            "m" => 60d,
            "h" => 3600d,
            "d" => 86400d,
            "w" => 604800d,
            _ => 0d
        };
        return seconds > 0;
    }

    public object? ReadYaml(IParser parser, Type type)
    {
        var scalar = parser.Consume<Scalar>();
        
        if (scalar.Value == null ||
            string.IsNullOrWhiteSpace(scalar.Value) ||
            scalar.Value == "null" ||
            scalar.Value == "~")
        {
            return null;
        }

        if (!TryParse(scalar.Value, out var result))
        {
            throw new FormatException($"Invalid time span format: {scalar.Value}. Expected format like '30d', '12h', '5d 6h' etc.");
        }

        return result;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        if (value is not TimeSpan timeSpan)
        {
            emitter.Emit(new Scalar("null"));
            return;
        }

        if (timeSpan < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Negative TimeSpan values are not supported.");
        }
        
        if (timeSpan == TimeSpan.Zero)
        {
            emitter.Emit(new Scalar("0s"));
            return;
        }
        
        var parts = new List<string>();
        var days = timeSpan.Days;
        var hours = timeSpan.Hours;
        var minutes = timeSpan.Minutes;
        var seconds = timeSpan.Seconds;

        if (days > 0)
        {
            parts.Add($"{days}d");
        }
        
        if (hours > 0)
        {
            parts.Add($"{hours}h");
        }
        
        if (minutes > 0)
        {
            parts.Add($"{minutes}m");
        }
        
        if (seconds > 0)
        {
            parts.Add($"{seconds}s");
        }
        
        var result = string.Join(" ", parts);
        emitter.Emit(new Scalar(result));
    }

    [GeneratedRegex(@"(\d+)([smhdw])", RegexOptions.Compiled)]
    private static partial Regex TimeSpanFormatRegex();

    [GeneratedRegex(@"^\s*(\d+[smhdw]\s*)+$", RegexOptions.Compiled)]
    private static partial Regex FullDurationFormatRegex();
} 