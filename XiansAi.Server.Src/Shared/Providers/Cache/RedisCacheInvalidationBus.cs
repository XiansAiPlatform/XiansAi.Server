using System.Text.Json;
using StackExchange.Redis;

namespace Shared.Providers;

/// <summary>
/// Transports cache invalidation envelopes between server instances over Redis pub/sub.
/// </summary>
public sealed class RedisCacheInvalidationBus : ICacheInvalidationBus, IHostedService
{
    public const string ChannelName = "xians:cache:invalidate";

    private static readonly RedisChannel Channel = RedisChannel.Literal(ChannelName);

    private readonly ISubscriber _subscriber;
    private readonly ICacheInvalidationApplicator _applicator;
    private readonly ILogger<RedisCacheInvalidationBus> _logger;
    private readonly Action<RedisChannel, RedisValue> _messageHandler;

    public RedisCacheInvalidationBus(
        IConnectionMultiplexer connectionMultiplexer,
        ICacheInvalidationApplicator applicator,
        ILogger<RedisCacheInvalidationBus> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        _applicator = applicator ?? throw new ArgumentNullException(nameof(applicator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriber = connectionMultiplexer.GetSubscriber();
        _messageHandler = HandleMessage;
    }

    public async Task PublishAsync(
        CacheInvalidationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Serialize(envelope);
            await _subscriber.PublishAsync(Channel, payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Invalidation is best-effort and must never break the business operation that caused it.
            _logger.LogWarning(ex, "Failed to publish cache invalidation to Redis");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _subscriber.SubscribeAsync(Channel, _messageHandler);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _subscriber.UnsubscribeAsync(Channel, _messageHandler);
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply cache invalidation received from Redis");
        }
    }
}
