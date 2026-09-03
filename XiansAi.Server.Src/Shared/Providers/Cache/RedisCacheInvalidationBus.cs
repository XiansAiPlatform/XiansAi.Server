using System.Text.Json;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Shared.Providers;

/// <summary>
/// Transports cache invalidation envelopes between server instances over Redis pub/sub.
/// </summary>
public sealed class RedisCacheInvalidationBus : ICacheInvalidationBus, IHostedService
{
    public const string ChannelName = "xians:cache:invalidate";

    private static readonly RedisChannel Channel = RedisChannel.Literal(ChannelName);

    private readonly Lazy<IConnectionMultiplexer> _connectionMultiplexer;
    private readonly ICacheInvalidationApplicator _applicator;
    private readonly ILogger<RedisCacheInvalidationBus> _logger;
    private readonly Action<RedisChannel, RedisValue> _messageHandler;

    public RedisCacheInvalidationBus(
        Lazy<IConnectionMultiplexer> connectionMultiplexer,
        ICacheInvalidationApplicator applicator,
        ILogger<RedisCacheInvalidationBus> logger)
    {
        _connectionMultiplexer = connectionMultiplexer ?? throw new ArgumentNullException(nameof(connectionMultiplexer));
        _applicator = applicator ?? throw new ArgumentNullException(nameof(applicator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messageHandler = HandleMessage;
    }

    private ISubscriber Subscriber => _connectionMultiplexer.Value.GetSubscriber();

    public async Task PublishAsync(
        CacheInvalidationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Serialize(envelope);
            await Subscriber.PublishAsync(Channel, payload).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Best-effort: cancellation of the publish path must not fail the business operation.
        }
        catch (RedisException ex)
        {
            // Invalidation is best-effort and must never break the business operation that caused it.
            _logger.LogWarning(ex, "Failed to publish cache invalidation to Redis");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to serialize cache invalidation envelope");
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Failed to publish cache invalidation; Redis connection disposed");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Starting Redis cache invalidation bus subscription");
        // Do not block host startup on Redis subscription; Kestrel must start even if pub/sub is slow.
        _ = SubscribeSafelyAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Subscriber.UnsubscribeAsync(Channel, _messageHandler);

    private async Task SubscribeSafelyAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await Subscriber.SubscribeAsync(Channel, _messageHandler).ConfigureAwait(false);
            _logger.LogInformation("Subscribed to Redis cache invalidation channel {Channel}", ChannelName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Subscription abandoned because the host is shutting down.
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Redis cache invalidation channel {Channel}", ChannelName);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Failed to subscribe to Redis cache invalidation; connection disposed");
        }
    }

    private void HandleMessage(RedisChannel channel, RedisValue payload)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CacheInvalidationEnvelope>(payload.ToString());
            if (envelope is null)
            {
                _logger.LogWarning("Ignoring empty cache invalidation envelope from Redis");
                return;
            }

            _applicator.Apply(envelope);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cache invalidation envelope from Redis");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Failed to apply cache invalidation received from Redis");
        }
    }
}
