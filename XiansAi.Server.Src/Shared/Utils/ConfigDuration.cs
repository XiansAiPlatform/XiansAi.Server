using Shared.Utils.Serialization;

namespace Shared.Utils;

public static class ConfigDuration
{
    /// <summary>
    /// Reads a duration such as "30d" or "5d 6h" from configuration. Returns null when the setting is
    /// missing or blank, or when it cannot be parsed and keeps the default value.
    /// </summary>
    public static TimeSpan? Get(IConfiguration configuration, string key, ILogger logger)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TimeSpanTypeConverter.TryParse(raw, out var duration))
        {
            return duration;
        }

        logger.LogWarning("Ignoring invalid duration '{Value}' for setting {Key}. Using the default. " +
                          "Expected a value like '30d', '12h' or '5d 6h'.",
            LogSanitizer.Sanitize(raw), key);
        return null;
    }
}
