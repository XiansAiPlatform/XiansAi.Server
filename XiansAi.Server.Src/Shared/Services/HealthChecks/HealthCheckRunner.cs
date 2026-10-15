using System.Diagnostics;
using Shared.Auth;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services.HealthChecks;

/// <summary>The user that scheduled health-check runs act as.</summary>
public static class HealthCheckSystemPrincipal
{
    public const string UserId = "system:health-check";

    /// <summary>
    /// Sets the tenant context to act as a tenant admin for <paramref name="tenantId"/> only.
    /// This is needed to read the agent's schedules and workflows.
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
    /// <summary>A result was saved.</summary>
    Recorded,

    /// <summary>Nothing ran: the check is deleted or disabled, or the activation is deleted or inactive.</summary>
    Skipped
}

public interface IHealthCheckRunner
{
    Task<HealthCheckRunOutcome> RunAsync(string tenantId, string checkId, string? workflowRunId, CancellationToken cancellationToken);
}

/// <summary>
/// Runs one scheduled check and saves the result.
/// Skips the run if the check or its activation is no longer active.
/// A failed check is saved as a result; it never throws.
/// </summary>
public class HealthCheckRunner : IHealthCheckRunner
{
    /// <summary>Extra time on top of the check's timeout before the run is stopped.</summary>
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

        // `check` still has the status from before this run. The notifier compares it with the new result.
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
