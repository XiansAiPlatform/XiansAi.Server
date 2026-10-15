namespace Shared.Configuration;

/// <summary>
/// Settings for agent health checks, from the "HealthChecks" section.
/// How long runs are kept is set with
/// <c>MongoIndexes__agent_health_check_runs__health_check_run_ttl__ExpireAfter</c>, not here.
/// </summary>
public class HealthChecksOptions
{
    public const string SectionName = "HealthChecks";

    /// <summary>The lowest interval allowed. Settings can't go below this.</summary>
    public const int AbsoluteMinIntervalMinutes = 5;

    public const int MaxIntervalMinutes = 1440;

    /// <summary>
    /// Turns health checks on or off. When off, nothing runs and creating or editing checks returns 503.
    /// Existing checks and runs can still be read.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The Temporal task queue for the server's health-check worker.</summary>
    public string TaskQueue { get; set; } = "xians-server-health-checks";

    /// <summary>
    /// The shortest interval allowed. Values below 5 minutes are raised to 5.
    /// </summary>
    public int MinIntervalMinutes { get; set; } = AbsoluteMinIntervalMinutes;

    /// <summary>The most checks one activation can have.</summary>
    public int MaxChecksPerActivation { get; set; } = 20;

    /// <summary>How often the reconciler fixes the Temporal schedules to match the saved checks.</summary>
    public int ReconcileIntervalMinutes { get; set; } = 60;

    public int EffectiveMinIntervalMinutes => Math.Max(AbsoluteMinIntervalMinutes, MinIntervalMinutes);
}
