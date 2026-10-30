using System.Text;
using Shared.Utils.Serialization;

namespace Shared.Configuration;

/// <summary>
/// Configuration for GDPR data subject requests (export and erasure).
/// </summary>
public class PrivacyOptions
{
    public const string SectionName = "Privacy";

    /// <summary>Shortest accepted <see cref="PseudonymKey"/>, in UTF-8 bytes. Matches the HMAC-SHA256 output size.</summary>
    public const int MinimumPseudonymKeyBytes = 32;

    /// <summary>
    /// Master switch. When false, no privacy services are registered, no routes are exposed and no job runs.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>How long an erasure waits before it runs. The requester can cancel during this window.</summary>
    public TimeSpan ErasureDelay { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// HMAC key for the subject hash kept on a request record after its identifiers are scrubbed.
    /// Changing it breaks matching of new requests against older records.
    /// </summary>
    public string? PseudonymKey { get; init; }

    /// <summary>How often the request processor polls for due erasures.</summary>
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>Processing attempts before an erasure is marked failed for operations follow-up.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>
    /// Reads the options. Durations use the server's duration format ("7d", "12h"), which the default
    /// options binder does not understand, so the section is read by hand.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Enabled"/> is not a boolean, or privacy is enabled and another setting is not a valid value.
    /// </exception>
    public static PrivacyOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var defaults = new PrivacyOptions();
        var enabled = section.GetValue(nameof(Enabled), defaults.Enabled);

        return new PrivacyOptions
        {
            Enabled = enabled,
            ErasureDelay = ReadDuration(section, nameof(ErasureDelay), enabled) ?? defaults.ErasureDelay,
            PseudonymKey = section[nameof(PseudonymKey)],
            PollIntervalSeconds = ReadInt(section, nameof(PollIntervalSeconds), enabled) ?? defaults.PollIntervalSeconds,
            MaxAttempts = ReadInt(section, nameof(MaxAttempts), enabled) ?? defaults.MaxAttempts
        };
    }

    // While enabled, fails instead of falling back to the default, because a mistyped cancellation window
    // should not go unnoticed. While disabled, a bad value must not stop the server from starting.
    private static TimeSpan? ReadDuration(IConfigurationSection section, string key, bool enabled)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TimeSpanTypeConverter.TryParse(raw, out var duration))
        {
            return duration;
        }

        if (!enabled)
        {
            return null;
        }

        throw new InvalidOperationException(
            $"{SectionName}:{key} is not a valid duration. Expected a value like '7d', '12h' or '5d 6h'.");
    }

    private static int? ReadInt(IConfigurationSection section, string key, bool enabled)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw, out var value))
        {
            return value;
        }

        if (!enabled)
        {
            return null;
        }

        throw new InvalidOperationException($"{SectionName}:{key} is not a whole number.");
    }

    /// <summary>
    /// Throws when the options cannot run safely. Only checked while <see cref="Enabled"/> is true.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(PseudonymKey) || Encoding.UTF8.GetByteCount(PseudonymKey) < MinimumPseudonymKeyBytes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(PseudonymKey)} must be set to at least {MinimumPseudonymKeyBytes} bytes " +
                $"when {SectionName}:{nameof(Enabled)} is true.");
        }

        if (ErasureDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(ErasureDelay)} must not be negative.");
        }

        if (PollIntervalSeconds < 1)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(PollIntervalSeconds)} must be at least 1.");
        }

        if (MaxAttempts < 1)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(MaxAttempts)} must be at least 1.");
        }
    }
}
