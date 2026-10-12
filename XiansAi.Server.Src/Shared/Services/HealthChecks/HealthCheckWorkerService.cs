using Shared.Configuration;
using Temporalio.Worker;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Hosts the server's Temporal worker for health-check runs on the platform namespace, and runs the
/// schedule reconciler on an interval. Every server instance polls the same task queue; Temporal hands
/// each scheduled run to exactly one of them. If the platform Temporal is unreachable, it retries
/// rather than failing host startup.
/// </summary>
public class HealthCheckWorkerService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstReconcileDelay = TimeSpan.FromSeconds(30);

    private readonly HealthChecksOptions _options;
    private readonly IHealthCheckTemporalClientProvider _clientProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HealthCheckWorkerService> _logger;

    public HealthCheckWorkerService(
        HealthChecksOptions options,
        IHealthCheckTemporalClientProvider clientProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<HealthCheckWorkerService> logger)
    {
        _options = options;
        _clientProvider = clientProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Health checks are disabled (HealthChecks:Enabled=false); worker not started.");
            return;
        }

        await Task.WhenAll(RunWorkerAsync(stoppingToken), RunReconcilerAsync(stoppingToken));
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        var activities = new HealthCheckActivities(_scopeFactory);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await _clientProvider.GetClientAsync(stoppingToken);
                using var worker = new TemporalWorker(client, new TemporalWorkerOptions(_options.TaskQueue)
                    .AddWorkflow<AgentHealthCheckWorkflow>()
                    .AddAllActivities(activities));

                _logger.LogInformation("Health-check worker polling task queue {TaskQueue}", _options.TaskQueue);
                await worker.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health-check worker stopped unexpectedly; restarting in {Delay}s", RetryDelay.TotalSeconds);
                if (!await DelayAsync(RetryDelay, stoppingToken)) break;
            }
        }
    }

    private async Task RunReconcilerAsync(CancellationToken stoppingToken)
    {
        if (!await DelayAsync(FirstReconcileDelay, stoppingToken)) return;

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.ReconcileIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IHealthCheckReconciler>().ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health-check reconcile pass failed");
            }

            if (!await DelayAsync(interval, stoppingToken)) break;
        }
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
