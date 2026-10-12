using System.Collections.Concurrent;
using Shared.Data.Models.HealthChecks;
using Shared.Services.HealthChecks;

namespace Tests.TestUtils;

/// <summary>
/// In-memory <see cref="IHealthCheckScheduler"/> for tests that run without a Temporal server.
/// Records the last desired state of each check's schedule so tests can assert pause/resume/delete.
/// </summary>
public sealed class FakeHealthCheckScheduler : IHealthCheckScheduler
{
    public sealed record ScheduledCheck(string TenantId, string CheckId, int IntervalMinutes, bool Paused);

    private readonly ConcurrentDictionary<string, ScheduledCheck> _schedules = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ScheduledCheck> Schedules => _schedules;

    /// <summary>When set, every call throws, simulating an unreachable Temporal.</summary>
    public bool Fail { get; set; }

    public ScheduledCheck? Get(string tenantId, string checkId)
        => _schedules.TryGetValue(HealthCheckScheduler.GetScheduleId(tenantId, checkId), out var schedule) ? schedule : null;

    public Task<HealthCheckScheduleState> UpsertAsync(AgentHealthCheck check, bool activationActive, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var paused = !(check.Enabled && activationActive);
        _schedules[HealthCheckScheduler.GetScheduleId(check.TenantId, check.Id)] =
            new ScheduledCheck(check.TenantId, check.Id, check.IntervalMinutes, paused);
        return Task.FromResult(paused ? HealthCheckScheduleState.Paused : HealthCheckScheduleState.Active);
    }

    public Task DeleteAsync(string tenantId, string checkId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        _schedules.TryRemove(HealthCheckScheduler.GetScheduleId(tenantId, checkId), out _);
        return Task.CompletedTask;
    }

    public Task<List<string>> ListScheduleIdsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult(_schedules.Keys.ToList());
    }

    /// <summary>Adds a schedule with no backing check, as left behind by a failed delete.</summary>
    public void AddOrphan(string tenantId, string checkId)
        => _schedules[HealthCheckScheduler.GetScheduleId(tenantId, checkId)] = new ScheduledCheck(tenantId, checkId, 5, false);

    private void ThrowIfFailing()
    {
        if (Fail)
        {
            throw new InvalidOperationException("Simulated Temporal outage");
        }
    }
}
