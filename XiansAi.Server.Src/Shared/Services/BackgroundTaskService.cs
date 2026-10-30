using System.Threading.Channels;

namespace Shared.Services;

/// <summary>
/// Interface for the background task service
/// </summary>
public interface IBackgroundTaskService
{
    void QueueDatabaseOperation(Func<Task> operation);
    Task WaitForCompletionAsync(TimeSpan timeout);
    bool HasPendingTasks { get; }
}

/// <summary>
/// Service for processing operations in the background without blocking HTTP connections
/// </summary>
/// <remarks>
/// On shutdown, queued work is finished instead of thrown away. This happens in
/// <see cref="StoppedAsync"/>, after the web server has stopped, so no new work can arrive.
/// </remarks>
public class BackgroundTaskService : BackgroundService, IBackgroundTaskService, IHostedLifecycleService
{
    public const string DrainTimeoutConfigKey = "Server:BackgroundTaskDrainTimeoutSeconds";
    public const int DefaultDrainTimeoutSeconds = 30;

    private readonly Channel<Func<Task>> _workItems;
    private readonly ILogger<BackgroundTaskService> _logger;
    private readonly TimeSpan _drainTimeout;
    private readonly CancellationTokenSource _abandonDrain = new();
    private int _pendingTaskCount = 0;
    private readonly SemaphoreSlim _completionSemaphore = new SemaphoreSlim(1, 1);

    public BackgroundTaskService(ILogger<BackgroundTaskService> logger, IConfiguration configuration)
        : this(logger, TimeSpan.FromSeconds(Math.Max(0,
            configuration.GetValue(DrainTimeoutConfigKey, DefaultDrainTimeoutSeconds))))
    {
    }

    internal BackgroundTaskService(ILogger<BackgroundTaskService> logger, TimeSpan drainTimeout)
    {
        _logger = logger;
        _drainTimeout = drainTimeout;

        // Create an unbounded channel to store work items
        _workItems = Channel.CreateUnbounded<Func<Task>>(
            new UnboundedChannelOptions
            {
                SingleReader = true, // Only one background task will process items
                SingleWriter = false // Multiple threads can queue items
            });
    }

    /// <summary>
    /// True if any work is waiting or running
    /// </summary>
    public bool HasPendingTasks => Volatile.Read(ref _pendingTaskCount) > 0;

    /// <summary>
    /// Waits for all background tasks to complete or until timeout
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    public async Task WaitForCompletionAsync(TimeSpan timeout)
    {
        // If no pending tasks, return immediately
        if (_pendingTaskCount == 0)
        {
            return;
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _completionSemaphore.WaitAsync(cts.Token);
            try
            {
                // Wait for all tasks to complete or timeout
                while (_pendingTaskCount > 0 && !cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(50, cts.Token);
                }
            }
            finally
            {
                _completionSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Wait for background tasks completion timed out after {Timeout}ms", timeout.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Queues a database operation to be executed in the background
    /// </summary>
    /// <param name="operation">The database operation to execute</param>
    public void QueueDatabaseOperation(Func<Task> operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        Interlocked.Increment(ref _pendingTaskCount);

        // Wrap the operation to decrement the counter when it completes
        async Task WrappedOperation()
        {
            try
            {
                await operation();
            }
            finally
            {
                Interlocked.Decrement(ref _pendingTaskCount);
            }
        }

        // Write the work item to the channel
        bool success = _workItems.Writer.TryWrite(WrappedOperation);

        if (!success)
        {
            Interlocked.Decrement(ref _pendingTaskCount);
            _logger.LogWarning("Failed to queue background work item: the queue is closed because the service is shutting down");
        }
    }

    /// <summary>
    /// Executes the background service
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task service is starting");

        // Keep running until the queue is closed and empty, so nothing is lost on shutdown.
        // Stop early only if finishing the queue takes too long.
        await foreach (var workItem in _workItems.Reader.ReadAllAsync())
        {
            if (_abandonDrain.IsCancellationRequested || stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await workItem();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing background work item");
            }
        }

        _logger.LogInformation("Background task service is stopping");
    }

    /// <summary>
    /// Does nothing on purpose. The web server is still finishing requests at this point and they
    /// may add more work. The queue is closed later, in <see cref="StoppedAsync"/>.
    /// </summary>
    public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called after the web server has stopped. Closes the queue and waits for the remaining
    /// work to finish, but no longer than the drain timeout.
    /// </summary>
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _workItems.Writer.TryComplete();

        var pending = Volatile.Read(ref _pendingTaskCount);
        if (pending > 0)
        {
            _logger.LogInformation(
                "Draining {PendingCount} background work item(s) before shutdown (timeout {TimeoutSeconds}s)",
                pending, _drainTimeout.TotalSeconds);
        }

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drainCts.CancelAfter(_drainTimeout);

        try
        {
            if (ExecuteTask != null)
            {
                await ExecuteTask.WaitAsync(drainCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _abandonDrain.Cancel();
            _logger.LogWarning(
                "Background task drain timed out; abandoning {PendingCount} unfinished work item(s)",
                Volatile.Read(ref _pendingTaskCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background task service faulted while draining its queue");
        }

        // Normal stop: signal the loop to end and wait for it.
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _abandonDrain.Dispose();
        _completionSemaphore.Dispose();
        base.Dispose();
    }
}
