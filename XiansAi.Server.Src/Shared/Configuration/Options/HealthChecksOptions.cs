namespace Shared.Configuration;

/// <summary>
/// Configuration for automated agent health checks. Bound from the "HealthChecks" configuration section.
/// Run-history retention is not configured here: it is the TTL of the <c>health_check_run_ttl</c> index,
/// overridable with <c>MongoIndexes__agent_health_check_runs__health_check_run_ttl__ExpireAfter</c>.
/// </summary>
public class HealthChecksOptions
{
    public const string SectionName = "HealthChecks";

    /// <summary>The floor no configuration can go below (R7).</summary>
    public const int AbsoluteMinIntervalMinutes = 5;

    public const int MaxIntervalMinutes = 1440;

    /// <summary>
    /// Master switch. When false, the Temporal worker and reconciler do not run and create/update
    /// requests are rejected with 503. Existing checks and their history stay readable.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Temporal task queue the server's health-check worker polls (platform namespace).</summary>
    public string TaskQueue { get; set; } = "xians-server-health-checks";

    /// <summary>
    /// Minimum run interval. Values below <see cref="AbsoluteMinIntervalMinutes"/> are raised to it.
    /// </summary>
    public int MinIntervalMinutes { get; set; } = AbsoluteMinIntervalMinutes;

    /// <summary>Maximum number of checks attached to a single activation.</summary>
    public int MaxChecksPerActivation { get; set; } = 20;

    /// <summary>How often the reconciler aligns Temporal schedules with the stored checks.</summary>
    public int ReconcileIntervalMinutes { get; set; } = 60;

    public int EffectiveMinIntervalMinutes => Math.Max(AbsoluteMinIntervalMinutes, MinIntervalMinutes);
}
