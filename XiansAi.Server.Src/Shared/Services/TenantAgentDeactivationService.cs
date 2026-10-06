using System.Threading.Channels;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// A request to deactivate every active agent of a disabled tenant.
/// </summary>
public record TenantAgentDeactivationRequest(string TenantId);

/// <summary>
/// Deactivates the agents of disabled tenants in the background.
/// </summary>
public interface ITenantAgentDeactivationService
{
    void Enqueue(TenantAgentDeactivationRequest request);
}

/// <summary>
/// Deactivates all active agents of a disabled tenant in the background, so disabling a tenant
/// returns immediately regardless of how many agents it has. Failures are logged; sending
/// <c>enabled: false</c> again re-queues the tenant. The queue is in memory and does not survive
/// a restart.
/// </summary>
public class TenantAgentDeactivationService : BackgroundService, ITenantAgentDeactivationService
{
    private readonly Channel<TenantAgentDeactivationRequest> _queue =
        Channel.CreateUnbounded<TenantAgentDeactivationRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantAgentDeactivationService> _logger;

    public TenantAgentDeactivationService(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantAgentDeactivationService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Enqueue(TenantAgentDeactivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_queue.Writer.TryWrite(request))
        {
            _logger.LogWarning("Failed to queue agent deactivation for tenant {TenantId}",
                LogSanitizer.Sanitize(request.TenantId));
            return;
        }

        _logger.LogInformation("Queued agent deactivation for disabled tenant {TenantId}",
            LogSanitizer.Sanitize(request.TenantId));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(request, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error deactivating agents for tenant {TenantId}",
                        LogSanitizer.Sanitize(request.TenantId));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>
    /// Deactivates every active agent of the tenant, provided it still exists and is still disabled.
    /// </summary>
    internal async Task ProcessAsync(TenantAgentDeactivationRequest request, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var tenant = await services.GetRequiredService<ITenantRepository>()
            .GetByTenantIdAsync(request.TenantId, cancellationToken);
        if (tenant == null)
        {
            _logger.LogInformation("Skipping agent deactivation: tenant {TenantId} no longer exists",
                LogSanitizer.Sanitize(request.TenantId));
            return;
        }
        if (tenant.Enabled)
        {
            _logger.LogInformation("Skipping agent deactivation: tenant {TenantId} was re-enabled",
                LogSanitizer.Sanitize(request.TenantId));
            return;
        }

        var active = await services.GetRequiredService<IActivationRepository>()
            .GetActiveActivationsAsync(request.TenantId);
        if (active.Count == 0)
        {
            _logger.LogInformation("No active agents to deactivate for tenant {TenantId}",
                LogSanitizer.Sanitize(request.TenantId));
            return;
        }

        var activationService = services.GetRequiredService<IActivationService>();
        var deactivated = 0;

        foreach (var activation in active)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await activationService.DeactivateAgentAsync(activation.Id, request.TenantId);
                if (result.IsSuccess)
                {
                    deactivated++;
                }
                else
                {
                    _logger.LogWarning("Failed to deactivate activation {ActivationName} ({ActivationId}) of tenant {TenantId}: {Error}",
                        LogSanitizer.Sanitize(activation.Name), LogSanitizer.Sanitize(activation.Id),
                        LogSanitizer.Sanitize(request.TenantId), LogSanitizer.Sanitize(result.ErrorMessage));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deactivating activation {ActivationName} ({ActivationId}) of tenant {TenantId}",
                    LogSanitizer.Sanitize(activation.Name), LogSanitizer.Sanitize(activation.Id),
                    LogSanitizer.Sanitize(request.TenantId));
            }
        }

        _logger.LogInformation("Deactivated {Deactivated}/{Total} agents of disabled tenant {TenantId}",
            deactivated, active.Count, LogSanitizer.Sanitize(request.TenantId));
    }
}
