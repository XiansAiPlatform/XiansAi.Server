using Shared.Configuration;
using Shared.Data.Models.HealthChecks;
using Shared.Utils;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Exceptions;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Keeps each check's Temporal schedule in line with the saved check.
/// If a call fails, the check is marked Pending and the reconciler tries again.
/// </summary>
public interface IHealthCheckScheduler
{
    /// <summary>Creates or updates the schedule. It only runs when the check is enabled and the activation is active.</summary>
    Task<HealthCheckScheduleState> UpsertAsync(AgentHealthCheck check, bool activationActive, CancellationToken cancellationToken = default);

    /// <summary>Deletes the schedule. It's fine if the schedule doesn't exist.</summary>
    Task DeleteAsync(string tenantId, string checkId, CancellationToken cancellationToken = default);

    /// <summary>The ids of all health-check schedules.</summary>
    Task<List<string>> ListScheduleIdsAsync(CancellationToken cancellationToken = default);
}

public class HealthCheckScheduler : IHealthCheckScheduler
{
    public const string ScheduleIdPrefix = "healthcheck:";

    /// <summary>
    /// Stops a run that was never picked up, so it doesn't block the next runs.
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

    /// <summary>A small random delay so checks with the same interval don't all run at once (max 1 minute).</summary>
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
            // Already deleted, so there is nothing to do.
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
                // Don't run all the missed runs after an outage. Just wait for the next one.
                CatchupWindow = TimeSpan.FromMinutes(1)
            },
            State = new ScheduleState { Paused = paused, Note = note }
        };
    }
}
