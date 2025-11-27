namespace Shared.Configuration;

/// <summary>
/// Configuration options for token usage limiting.
/// </summary>
public class TokenUsageOptions
{
    public const string SectionName = "TokenUsage";

    /// <summary>
    /// Master switch for the entire feature.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default max tokens per tenant when no custom limit exists.
    /// </summary>
    public long DefaultTenantLimit { get; set; } = 200_000;

    /// <summary>
    /// Default rolling window length in seconds (24h).
    /// </summary>
    public int WindowSeconds { get; set; } = 86_400;

    /// <summary>
    /// Threshold (0-1) where warnings should be triggered.
    /// </summary>
    public double WarningPercentage { get; set; } = 0.8;

    /// <summary>
    /// Whether to persist detailed usage events for auditing.
    /// </summary>
    public bool RecordUsageEvents { get; set; } = true;

    /// <summary>
    /// How many days of usage history the UI/API may request.
    /// </summary>
    public int MaxUsageHistoryDays { get; set; } = 30;

    /// <summary>
    /// Whether tenant admins can define per-user overrides.
    /// </summary>
    public bool AllowUserOverrides { get; set; } = true;
}

