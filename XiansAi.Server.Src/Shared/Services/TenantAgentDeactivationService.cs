using System.Collections.Concurrent;
using System.Threading.Channels;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Queues a disabled tenant's agents for deactivation, off the HTTP request path.
/// </summary>
public interface ITenantAgentDeactivationQueue
{
    /// <summary>
    /// Queues deactivation of every active activation of <paramref name="tenantId"/>.
    /// Returns false when that tenant is already queued or being processed.
    /// </summary>
    bool Enqueue(string tenantId, string requestedBy);
}

/// <summary>
/// Deactivates the agents of disabled tenants in the background.
///
/// Deactivating an activation makes real Temporal calls (cancel, wait, terminate, delete schedules)
/// and takes a few seconds each, so doing it inside the disable request could outlast client/proxy
/// timeouts. The request queues the tenant here and returns. Jobs run one at a time on their own
/// queue (not the shared <see cref="BackgroundTaskService"/>, which carries conversation writes),
/// each in a fresh DI scope whose tenant context carries the admin who disabled the tenant.
///
/// Best-effort: failures are logged only. The queue is in memory, so jobs not yet finished when the
/// server stops are lost; re-sending enabled=false for the tenant queues it again.
/// </summary>
public sealed class TenantAgentDeactivationService : BackgroundService, ITenantAgentDeactivationQueue
{
    private readonly record struct Job(string TenantId, string RequestedBy);

    private readonly Channel<Job> _jobs = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>Tenants queued or in progress, so repeated disables don't pile up jobs.</summary>
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantAgentDeactivationService> _logger;

    public TenantAgentDeactivationService(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantAgentDeactivationService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool Enqueue(string tenantId, string requestedBy)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || !_pending.TryAdd(tenantId, 0))
        {
            return false;
        }

        if (!_jobs.Writer.TryWrite(new Job(tenantId, requestedBy)))
        {
            _pending.TryRemove(tenantId, out _);
            _logger.LogWarning("Failed to queue agent deactivation for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return false;
        }

        _logger.LogInformation("Queued agent deactivation for disabled tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await DeactivateTenantAgentsAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error deactivating agents of disabled tenant {TenantId}", LogSanitizer.Sanitize(job.TenantId));
                }
                finally
                {
                    _pending.TryRemove(job.TenantId, out _);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task DeactivateTenantAgentsAsync(Job job, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        // Each deactivation writes an activation.deactivated audit entry/webhook, which reads the
        // actor and tenant from the scoped tenant context.
        var tenantContext = services.GetRequiredService<ITenantContext>();
        tenantContext.TenantId = job.TenantId;
        tenantContext.LoggedInUser = job.RequestedBy;
        tenantContext.UserRoles = new[] { SystemRoles.SysAdmin };
        tenantContext.AuthorizedTenantIds = new[] { job.TenantId };

        var tenant = await services.GetRequiredService<ITenantRepository>().GetByTenantIdAsync(job.TenantId, stoppingToken);
        if (tenant == null || tenant.Enabled)
        {
            // Deleted, or re-enabled before the job ran: leave its agents alone.
            return;
        }

        var activations = await services.GetRequiredService<IActivationRepository>().GetActiveActivationsAsync(job.TenantId);
        if (activations == null || activations.Count == 0)
        {
            return;
        }

        var activationService = services.GetRequiredService<IActivationService>();
        var deactivated = 0;
        var failed = 0;

        foreach (var activation in activations)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                var result = await activationService.DeactivateAgentAsync(activation.Id, job.TenantId);
                if (result.IsSuccess)
                {
                    deactivated++;
                }
                else
                {
                    failed++;
                    _logger.LogWarning("Failed to deactivate activation {ActivationName} for agent {AgentName} in disabled tenant {TenantId}: {Error}",
                        LogSanitizer.Sanitize(activation.Name), LogSanitizer.Sanitize(activation.AgentName), LogSanitizer.Sanitize(job.TenantId), LogSanitizer.Sanitize(result.ErrorMessage));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex, "Error deactivating activation {ActivationName} for agent {AgentName} in disabled tenant {TenantId}",
                    LogSanitizer.Sanitize(activation.Name), LogSanitizer.Sanitize(activation.AgentName), LogSanitizer.Sanitize(job.TenantId));
            }
        }

        _logger.LogInformation(
            "Deactivated activations for disabled tenant {TenantId}: {Deactivated} deactivated, {Failed} failed",
            LogSanitizer.Sanitize(job.TenantId), deactivated, failed);
    }
}
