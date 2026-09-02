using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Shared.Repositories;
using StackExchange.Redis;

namespace Shared.Services;

/// <summary>
/// Coordinates pending requests through Redis result keys and completion signals.
/// </summary>
public sealed class RedisPendingRequestCoordinator : IPendingRequestCoordinator, IHostedService
{
    public const string CompletionChannelName = "xians:pending:complete";
    public const string ResultKeyPrefix = "xians:pending:result:";
    private static readonly TimeSpan ResultExpiry = TimeSpan.FromSeconds(300);
    private static readonly RedisChannel CompletionChannel =
        RedisChannel.Literal(CompletionChannelName);

    private readonly Lazy<IConnectionMultiplexer> _connectionMultiplexer;
    private readonly ILogger<RedisPendingRequestCoordinator> _logger;
    private readonly Action<RedisChannel, RedisValue> _messageHandler;

    public RedisPendingRequestCoordinator(
        Lazy<IConnectionMultiplexer> connectionMultiplexer,
        ILogger<RedisPendingRequestCoordinator> logger)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageHandler = HandleCompletionSignal;
    }

    private IDatabase Database => _connectionMultiplexer.Value.GetDatabase();
    private ISubscriber Subscriber => _connectionMultiplexer.Value.GetSubscriber();

    public event Action<string, ConversationMessage, MessageType?>? CompletionReceived;

    public async Task AnnounceWaitAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RetrieveAndNotifyAsync(requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to check Redis for pending request {RequestId}", requestId);
        }
    }

    public async Task PublishCompletionAsync(
        string requestId,
        ConversationMessage response,
        MessageType? messageType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new PendingRequestResult(response, messageType);
            var payload = JsonSerializer.Serialize(result);
            await Database.StringSetAsync(
                GetResultKey(requestId),
                payload,
                ResultExpiry,
                When.Always,
                CommandFlags.None).ConfigureAwait(false);
            var signal = JsonSerializer.Serialize(new CompletionSignal(requestId));
            await Subscriber.PublishAsync(CompletionChannel, signal).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish pending request completion to Redis for {RequestId}",
                requestId);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting Redis pending-request completion subscription");
        // Do not block host startup on Redis subscription; Kestrel must start even if pub/sub is slow.
        _ = SubscribeSafelyAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Subscriber.UnsubscribeAsync(CompletionChannel, _messageHandler);

    private async Task SubscribeSafelyAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Subscriber.SubscribeAsync(CompletionChannel, _messageHandler).ConfigureAwait(false);
            _logger.LogInformation(
                "Subscribed to Redis pending-request completion channel {Channel}",
                CompletionChannelName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to subscribe to Redis pending-request completion channel {Channel}",
                CompletionChannelName);
        }
    }

    private void HandleCompletionSignal(RedisChannel channel, RedisValue payload)
    {
        try
        {
            var signal = JsonSerializer.Deserialize<CompletionSignal>(payload.ToString());
            if (signal is null || string.IsNullOrWhiteSpace(signal.RequestId))
            {
                _logger.LogWarning("Ignoring invalid pending request completion signal");
                return;
            }

            _ = RetrieveAndNotifyAsync(signal.RequestId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process pending request completion signal");
        }
    }

    private async Task RetrieveAndNotifyAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await Database
                .StringGetAsync(GetResultKey(requestId), CommandFlags.None)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!payload.HasValue)
            {
                return;
            }

            var result = JsonSerializer.Deserialize<PendingRequestResult>(payload.ToString());
            if (result?.Response is null)
            {
                _logger.LogWarning(
                    "Ignoring invalid pending request result for {RequestId}", requestId);
                return;
            }

            CompletionReceived?.Invoke(requestId, result.Response, result.MessageType);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to retrieve pending request result from Redis for {RequestId}",
                requestId);
        }
    }

    private static RedisKey GetResultKey(string requestId) =>
        $"{ResultKeyPrefix}{requestId}";

    private sealed record CompletionSignal(string RequestId);

    private sealed record PendingRequestResult(
        ConversationMessage Response,
        MessageType? MessageType);
}
