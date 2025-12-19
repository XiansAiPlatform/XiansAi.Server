namespace Shared.Configuration;

/// <summary>
/// Configuration options for usage event tracking.
/// </summary>
public class UsageEventsOptions
{
    public const string SectionName = "UsageEvents";

    /// <summary>
    /// Whether to persist detailed usage events for auditing.
    /// </summary>
    public bool RecordUsageEvents { get; set; } = true;
}

