using System.Diagnostics;
using Shared.Auth;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services.HealthChecks;

/// <summary>The identity scheduled health-check runs execute as.</summary>
public static class HealthCheckSystemPrincipal
{
    public const string UserId = "system:health-check";

    /// <summary>
    /// Makes a fresh scope's tenant context act for <paramref name="tenantId"/>. TenantAdmin is
    /// scoped to that tenant alone and mirrors the only role allowed to configure checks
    /// (<c>tenant.healthChecks.manage</c>); it is what agent-permission checks such as reading a
    /// schedule require.
    /// </summary>
    public static void Apply(ITenantContext tenantContext, string tenantId)
    {
        tenantContext.TenantId = tenantId;
        tenantContext.LoggedInUser = UserId;
        tenantContext.ParticipantId = ChatHealthCheckExecutor.ParticipantId;
        tenantContext.UserType = UserType.Unknown;
        tenantContext.UserRoles = [SystemRoles.TenantAdmin];
        tenantContext.AuthorizedTenantIds = [tenantId];
    }
}

public enum HealthCheckRunOutcome
{
    /// <summary>A result was persisted.</summary>
    Recorded,

    /// <summary>Nothing ran: the check was deleted or disabled, or its activation is gone or inactive.</summary>
    Skipped
}

public interface IHealthCheckRunner
{
    Task<HealthCheckRunOutcome> RunAsync(string tenantId, string checkId, string? workflowRunId, CancellationToken cancellationToken);
}

/// <summary>
/// Executes one scheduled run: re-reads the check and its activation, skips when either is no
/// longer eligible (R8; also covers a schedule firing while it is being paused), runs the
/// type-specific executor under the check's timeout, and records the result. Check failures are
/// results, never exceptions.
/// </summary>
public class HealthCheckRunner : IHealthCheckRunner
{
    /// <summary>Slack over the check's own timeout before the run is abandoned.</summary>
    private static readonly TimeSpan TimeoutGrace = TimeSpan.FromSeconds(15);

    private readonly IAgentHealthCheckRepository _repository;
    private readonly IActivationRepository _activationRepository;
    private readonly IEnumerable<IHealthCheckExecutor> _executors;
    private readonly IHealthCheckNotifier _notifier;
    private readonly ILogger<HealthCheckRunner> _logger;

    public HealthCheckRunner(
        IAgentHealthCheckRepository repository,
        IActivationRepository activationRepository,
        IEnumerable<IHealthCheckExecutor> executors,
        IHealthCheckNotifier notifier,
        ILogger<HealthCheckRunner> logger)
    {
        _repository = repository;
        _activationRepository = activationRepository;
        _executors = executors;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<HealthCheckRunOutcome> RunAsync(
        string tenantId, string checkId, string? workflowRunId, CancellationToken cancellationToken)
    {
        var check = await _repository.GetByIdAsync(tenantId, checkId);
        if (check == null || !check.Enabled)
        {
            _logger.LogInformation("Skipping health check {CheckId}: {Reason}",
                LogSanitizer.Sanitize(checkId), check == null ? "deleted" : "disabled");
            return HealthCheckRunOutcome.Skipped;
        }

        var activation = await _activationRepository.GetByIdAsync(check.ActivationId);
        if (activation == null || activation.TenantId != tenantId || !activation.IsActive)
        {
            _logger.LogInformation("Skipping health check {CheckId}: activation {ActivationId} is {Reason}",
                LogSanitizer.Sanitize(checkId), LogSanitizer.Sanitize(check.ActivationId),
                activation == null || activation.TenantId != tenantId ? "missing" : "inactive");
            return HealthCheckRunOutcome.Skipped;
        }

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = await ExecuteAsync(check, cancellationToken);
        stopwatch.Stop();

        var detail = Truncate(result.Detail, AgentHealthCheckRun.MaxDetailLength);
        var run = new AgentHealthCheckRun
        {
            TenantId = tenantId,
            CheckId = check.Id,
            ActivationId = check.ActivationId,
            Type = check.Type,
            Status = result.Status,
            StartedAt = startedAt,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Detail = detail,
            ResponseSnippet = result.ResponseSnippet,
            AssertionResults = result.AssertionResults?.ToList() ?? new(),
            WorkflowRunId = workflowRunId
        };
        await _repository.InsertRunAsync(run);
        await _repository.RecordRunResultAsync(check.Id, result.Status, startedAt, detail);

        // `check` still holds the status from before this run, which is what the notifier compares against.
        await _notifier.NotifyAsync(check, run);

        _logger.LogInformation("Health check {CheckId} ({Type}) for {Agent}/{Activation}: {Status} in {DurationMs}ms",
            LogSanitizer.Sanitize(check.Id), check.Type, LogSanitizer.Sanitize(check.AgentName),
            LogSanitizer.Sanitize(check.ActivationName), result.Status, stopwatch.ElapsedMilliseconds);

        return HealthCheckRunOutcome.Recorded;
    }

    private async Task<HealthCheckExecutionResult> ExecuteAsync(AgentHealthCheck check, CancellationToken cancellationToken)
    {
        var executor = _executors.FirstOrDefault(e => e.Type == check.Type);
        if (executor == null)
        {
            return HealthCheckExecutionResult.Error($"No executor registered for check type '{check.Type}'");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(check.TimeoutSeconds) + TimeoutGrace);

        try
        {
            return await executor.ExecuteAsync(check, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckExecutionResult.Error($"Timed out after {check.TimeoutSeconds}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Health check {CheckId} threw during execution", LogSanitizer.Sanitize(check.Id));
            return HealthCheckExecutionResult.Error($"Check failed to execute: {ex.Message}");
        }
    }

    private static string? Truncate(string? value, int maxLength)
        => value == null || value.Length <= maxLength ? value : value[..maxLength];
}
