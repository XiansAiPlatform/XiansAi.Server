using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Rewrites legacy conversation messages that cannot be unread to Read after the host is up.
/// Only outgoing chat and file messages are allowed to stay unread. The scan is claimed once,
/// checkpointed by _id, and resumed after a failure without starting from the first document.
/// </summary>
public class ConversationMessageReadStatusBackfillService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationMessageReadStatusBackfillService> _logger;
    private readonly string _ownerId = Guid.NewGuid().ToString("N");

    public ConversationMessageReadStatusBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<ConversationMessageReadStatusBackfillService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield before any Mongo call so this worker cannot block Kestrel from binding.
        await Task.Yield();

        _logger.LogInformation("Conversation message read-status backfill worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
                await repository.BackfillMessagesThatCannotBeUnreadOnceAsync(stoppingToken, _ownerId);
                _logger.LogInformation("Conversation message read-status backfill worker finished");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Conversation message read-status backfill stopping; checkpoint kept");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Conversation message read-status backfill failed; retrying in {DelaySeconds}s with the checkpoint kept",
                    RetryDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Conversation message read-status backfill stopping; checkpoint kept");
                return;
            }
        }
    }
}
