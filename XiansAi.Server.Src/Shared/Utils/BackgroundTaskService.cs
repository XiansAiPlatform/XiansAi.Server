using System.Threading.Channels;
using System.Threading.Tasks;
using System.Threading;

namespace XiansAi.Server.Utils;

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
public class BackgroundTaskService : BackgroundService, IBackgroundTaskService
{
    private readonly Channel<Func<Task>> _workItems;
    private readonly ILogger<BackgroundTaskService> _logger;
    private int _pendingTaskCount = 0;
    private readonly SemaphoreSlim _completionSemaphore = new SemaphoreSlim(1, 1);

    public BackgroundTaskService(ILogger<BackgroundTaskService> logger)
    {
        _logger = logger;
        
        // Create an unbounded channel to store work items
        _workItems = Channel.CreateUnbounded<Func<Task>>(
            new UnboundedChannelOptions
            {
                SingleReader = true, // Only one background task will process items
                SingleWriter = false // Multiple threads can queue items
            });
    }

    /// <summary>
    /// Gets whether there are any pending tasks in the queue
    /// </summary>
    public bool HasPendingTasks => _pendingTaskCount > 0 || !_workItems.Reader.Completion.IsCompleted;

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
            _logger.LogWarning("Failed to queue background work item");
        }
    }

    /// <summary>
    /// Executes the background service
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background task service is starting");

        // Process work items until the app shuts down
        await foreach (var workItem in _workItems.Reader.ReadAllAsync(stoppingToken))
        {
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
} 