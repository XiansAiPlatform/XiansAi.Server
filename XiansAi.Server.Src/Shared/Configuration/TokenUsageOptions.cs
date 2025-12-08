namespace Shared.Configuration;

/// <summary>
/// Configuration options for token usage event tracking.
/// </summary>
public class TokenUsageOptions
{
    public const string SectionName = "TokenUsage";

    /// <summary>
    /// Master switch for the entire feature.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to persist detailed usage events for auditing.
    /// </summary>
    public bool RecordUsageEvents { get; set; } = true;
}

