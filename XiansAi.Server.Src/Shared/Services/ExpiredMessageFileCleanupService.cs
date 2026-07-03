namespace Shared.Services;

/// <summary>
/// Background worker that removes expired message file attachments from GridFS.
///
/// The conversation_message documents that reference these files are removed automatically by a
/// MongoDB TTL index, but GridFS stores each file as two documents (a metadata doc in
/// message_files.files and the bytes in message_files.chunks). A native TTL index only deletes
/// documents in the collection it is placed on and does not cascade, so a TTL on the files
/// collection would orphan the chunk documents. This sweeper instead finds files whose
/// metadata.expires_at has passed and deletes them through the bucket, which removes both the
/// file document and its chunks.
/// </summary>
public class ExpiredMessageFileCleanupService : BackgroundService
{
    /// <summary>How often to sweep for expired files.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    /// <summary>Delay before the first sweep so it does not compete with startup work.</summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    /// <summary>Maximum files deleted per query. The sweep loops until a partial batch is returned.</summary>
    private const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredMessageFileCleanupService> _logger;

    public ExpiredMessageFileCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredMessageFileCleanupService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Expired message file cleanup started (interval {IntervalHours}h, batch {BatchSize}).",
            SweepInterval.TotalHours, BatchSize);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during expired message file cleanup");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Expired message file cleanup stopping.");
    }

    /// <summary>
    /// Deletes expired files in batches until fewer than a full batch remains, so a large backlog
    /// clears within a single sweep instead of one batch per interval.
    /// </summary>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IMessageFileStorage>();

        var totalDeleted = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var deleted = await fileStorage.DeleteExpiredAsync(DateTime.UtcNow, BatchSize, stoppingToken);
            totalDeleted += deleted;

            if (deleted < BatchSize)
            {
                break;
            }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation("Expired message file cleanup removed {Count} file(s).", totalDeleted);
        }
    }
}
