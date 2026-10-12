using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services.HealthChecks;

public sealed record HealthCheckReconcileResult(int Synced, int OrphanChecksDeleted, int OrphanSchedulesDeleted, int Failures);

public interface IHealthCheckReconciler
{
    Task<HealthCheckReconcileResult> ReconcileAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Closes the gaps left by partial failures, since Mongo writes and Temporal calls are not atomic:
/// <list type="bullet">
/// <item>a check whose schedule is pending, missing, or in the wrong paused state is re-synced;</item>
/// <item>a check whose activation no longer exists is deleted (activation delete cleanup failed);</item>
/// <item>a <c>healthcheck:</c> schedule with no matching check is deleted.</item>
/// </list>
/// Every step is idempotent, so concurrent runs from several server instances are harmless.
/// </summary>
public class HealthCheckReconciler : IHealthCheckReconciler
{
    private readonly IAgentHealthCheckRepository _repository;
    private readonly IActivationRepository _activationRepository;
    private readonly IHealthCheckScheduler _scheduler;
    private readonly ILogger<HealthCheckReconciler> _logger;

    public HealthCheckReconciler(
        IAgentHealthCheckRepository repository,
        IActivationRepository activationRepository,
        IHealthCheckScheduler scheduler,
        ILogger<HealthCheckReconciler> logger)
    {
        _repository = repository;
        _activationRepository = activationRepository;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<HealthCheckReconcileResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var checks = await _repository.ListAllAsync();
        var scheduleIds = (await _scheduler.ListScheduleIdsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var activations = new Dictionary<string, AgentActivation?>(StringComparer.Ordinal);
        var expectedScheduleIds = new HashSet<string>(StringComparer.Ordinal);
        int synced = 0, orphanChecks = 0, orphanSchedules = 0, failures = 0;

        foreach (var check in checks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!activations.TryGetValue(check.ActivationId, out var activation))
                {
                    activation = await _activationRepository.GetByIdAsync(check.ActivationId);
                    activations[check.ActivationId] = activation;
                }

                if (activation == null || activation.TenantId != check.TenantId)
                {
                    // The schedule, if any, is not added to expectedScheduleIds and goes in the orphan sweep.
                    await _repository.DeleteRunsByCheckAsync(check.TenantId, check.Id);
                    await _repository.DeleteAsync(check.TenantId, check.Id);
                    orphanChecks++;
                    continue;
                }

                var scheduleId = HealthCheckScheduler.GetScheduleId(check.TenantId, check.Id);
                expectedScheduleIds.Add(scheduleId);

                var desired = check.Enabled && activation.IsActive
                    ? HealthCheckScheduleState.Active
                    : HealthCheckScheduleState.Paused;

                if (check.ScheduleState != desired || !scheduleIds.Contains(scheduleId))
                {
                    var state = await _scheduler.UpsertAsync(check, activation.IsActive, cancellationToken);
                    await _repository.SetScheduleStateAsync(check.Id, state);
                    synced++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(ex, "Health-check reconcile failed for check {CheckId}", LogSanitizer.Sanitize(check.Id));
            }
        }

        foreach (var scheduleId in scheduleIds.Where(id => !expectedScheduleIds.Contains(id)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (tenantId, checkId) = ParseScheduleId(scheduleId);
            if (tenantId == null || checkId == null)
            {
                continue;
            }

            try
            {
                await _scheduler.DeleteAsync(tenantId, checkId, cancellationToken);
                orphanSchedules++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                _logger.LogWarning(ex, "Could not delete orphan health-check schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
            }
        }

        var result = new HealthCheckReconcileResult(synced, orphanChecks, orphanSchedules, failures);
        _logger.LogInformation(
            "Health-check reconcile: {Checks} checks, {Synced} schedules synced, {OrphanChecks} orphan checks and {OrphanSchedules} orphan schedules deleted, {Failures} failures",
            checks.Count, synced, orphanChecks, orphanSchedules, failures);
        return result;
    }

    /// <summary>Splits <c>healthcheck:{tenantId}:{checkId}</c>; the check id is the last segment (an ObjectId).</summary>
    public static (string? TenantId, string? CheckId) ParseScheduleId(string scheduleId)
    {
        if (!scheduleId.StartsWith(HealthCheckScheduler.ScheduleIdPrefix, StringComparison.Ordinal))
        {
            return (null, null);
        }

        var rest = scheduleId[HealthCheckScheduler.ScheduleIdPrefix.Length..];
        var separator = rest.LastIndexOf(':');
        if (separator <= 0 || separator == rest.Length - 1)
        {
            return (null, null);
        }

        return (rest[..separator], rest[(separator + 1)..]);
    }
}
