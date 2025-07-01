using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Shared.Utils.Serialization;

public partial class TimeSpanTypeConverter : IYamlTypeConverter
{
    private static readonly Regex TimeSpanRegex = TimeSpanFormatRegex();
    
    public bool Accepts(Type type)
    {
        return type == typeof(TimeSpan) || type == typeof(TimeSpan?);
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

        var value = scalar.Value;
        var totalTimeSpan = TimeSpan.Zero;
    
        var matches = TimeSpanRegex.Matches(value);
        var replaced = TimeSpanRegex.Replace(value, "").Trim();
        
        if (matches.Count is 0 || !string.IsNullOrEmpty(replaced))
        {
            throw new FormatException($"Invalid time span format: {value}. Expected format like '30d', '12h', '5d 6h' etc.");
        }

        foreach (Match match in matches)
        {
            var number = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;
        
            totalTimeSpan += unit switch
            {
                "s" => TimeSpan.FromSeconds(number),
                "m" => TimeSpan.FromMinutes(number),
                "h" => TimeSpan.FromHours(number),
                "d" => TimeSpan.FromDays(number),
                "w" => TimeSpan.FromDays(number * 7),
                // This case should not be reachable due to the regex pattern
                _ => throw new FormatException($"Invalid time unit: {unit}")
            };
        }
        return totalTimeSpan;
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
} 