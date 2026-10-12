using Shared.Configuration;
using Shared.Data.Models.HealthChecks;
using Shared.Utils;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Exceptions;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Keeps each check's Temporal Schedule in the platform namespace in line with its stored
/// definition. Mongo is the source of truth; callers record <see cref="HealthCheckScheduleState.Pending"/>
/// when a call here fails and the reconciler retries.
/// </summary>
public interface IHealthCheckScheduler
{
    /// <summary>Creates or updates the schedule. Runs only when the check is enabled and its activation active.</summary>
    Task<HealthCheckScheduleState> UpsertAsync(AgentHealthCheck check, bool activationActive, CancellationToken cancellationToken = default);

    /// <summary>Deletes the schedule; a missing schedule is not an error.</summary>
    Task DeleteAsync(string tenantId, string checkId, CancellationToken cancellationToken = default);

    /// <summary>Ids of every health-check schedule in the platform namespace.</summary>
    Task<List<string>> ListScheduleIdsAsync(CancellationToken cancellationToken = default);
}

public class HealthCheckScheduler : IHealthCheckScheduler
{
    public const string ScheduleIdPrefix = "healthcheck:";

    /// <summary>
    /// Kills a run that never got picked up (e.g. no server instance polling the queue) so the
    /// overlap=Skip policy does not block every later run behind it.
    /// </summary>
    private static readonly TimeSpan RunExecutionTimeout = TimeSpan.FromMinutes(10);

    private readonly IHealthCheckTemporalClientProvider _clientProvider;
    private readonly HealthChecksOptions _options;
    private readonly ILogger<HealthCheckScheduler> _logger;

    public HealthCheckScheduler(
        IHealthCheckTemporalClientProvider clientProvider,
        HealthChecksOptions options,
        ILogger<HealthCheckScheduler> logger)
    {
        _clientProvider = clientProvider;
        _options = options;
        _logger = logger;
    }

    public static string GetScheduleId(string tenantId, string checkId) => $"{ScheduleIdPrefix}{tenantId}:{checkId}";

    /// <summary>Spreads runs of checks sharing an interval: up to a tenth of it, capped at one minute.</summary>
    public static TimeSpan GetJitter(int intervalMinutes)
        => TimeSpan.FromSeconds(Math.Min(60, intervalMinutes * 60 / 10));

    public async Task<HealthCheckScheduleState> UpsertAsync(
        AgentHealthCheck check, bool activationActive, CancellationToken cancellationToken = default)
    {
        var client = await _clientProvider.GetClientAsync(cancellationToken);
        var scheduleId = GetScheduleId(check.TenantId, check.Id);
        var paused = !(check.Enabled && activationActive);
        var note = !check.Enabled ? "health check disabled" : !activationActive ? "activation deactivated" : null;
        var schedule = BuildSchedule(check, paused, note);

        try
        {
            await client.CreateScheduleAsync(scheduleId, schedule, new ScheduleOptions
            {
                Rpc = new RpcOptions { CancellationToken = cancellationToken }
            });
            _logger.LogInformation("Created health-check schedule {ScheduleId} (paused: {Paused})",
                LogSanitizer.Sanitize(scheduleId), paused);
        }
        catch (ScheduleAlreadyRunningException)
        {
            await client.GetScheduleHandle(scheduleId).UpdateAsync(
                _ => new ScheduleUpdate(schedule),
                new RpcOptions { CancellationToken = cancellationToken });
            _logger.LogInformation("Updated health-check schedule {ScheduleId} (paused: {Paused})",
                LogSanitizer.Sanitize(scheduleId), paused);
        }

        return paused ? HealthCheckScheduleState.Paused : HealthCheckScheduleState.Active;
    }

    public async Task DeleteAsync(string tenantId, string checkId, CancellationToken cancellationToken = default)
    {
        var client = await _clientProvider.GetClientAsync(cancellationToken);
        var scheduleId = GetScheduleId(tenantId, checkId);

        try
        {
            await client.GetScheduleHandle(scheduleId).DeleteAsync(new RpcOptions { CancellationToken = cancellationToken });
            _logger.LogInformation("Deleted health-check schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            // Already gone (never created, or deleted by an earlier attempt): nothing to do.
        }
    }

    public async Task<List<string>> ListScheduleIdsAsync(CancellationToken cancellationToken = default)
    {
        var client = await _clientProvider.GetClientAsync(cancellationToken);
        var ids = new List<string>();

        await foreach (var entry in client.ListSchedulesAsync(new ScheduleListOptions
        {
            Rpc = new RpcOptions { CancellationToken = cancellationToken }
        }))
        {
            if (entry.Id.StartsWith(ScheduleIdPrefix, StringComparison.Ordinal))
            {
                ids.Add(entry.Id);
            }
        }

        return ids;
    }

    private Schedule BuildSchedule(AgentHealthCheck check, bool paused, string? note)
    {
        var action = ScheduleActionStartWorkflow.Create(
            (AgentHealthCheckWorkflow workflow) => workflow.RunAsync(check.TenantId, check.Id),
            new WorkflowOptions($"healthcheck-run:{check.TenantId}:{check.Id}", _options.TaskQueue)
            {
                ExecutionTimeout = RunExecutionTimeout
            });

        return new Schedule(action, new ScheduleSpec
        {
            Intervals = [new ScheduleIntervalSpec(TimeSpan.FromMinutes(check.IntervalMinutes))],
            Jitter = GetJitter(check.IntervalMinutes)
        })
        {
            Policy = new SchedulePolicy
            {
                Overlap = ScheduleOverlapPolicy.Skip,
                // Don't replay missed runs in a burst after an outage; the next interval is enough.
                CatchupWindow = TimeSpan.FromMinutes(1)
            },
            State = new ScheduleState { Paused = paused, Note = note }
        };
    }
}
