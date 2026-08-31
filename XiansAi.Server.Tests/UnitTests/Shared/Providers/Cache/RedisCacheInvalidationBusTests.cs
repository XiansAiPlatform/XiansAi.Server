using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Providers;
using StackExchange.Redis;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class RedisCacheInvalidationBusTests
{
    [Fact]
    public async Task PublishAsync_SerializesEnvelopeToExpectedChannel()
    {
        var subscriber = new Mock<ISubscriber>();
        RedisChannel publishedChannel = default;
        RedisValue publishedValue = default;
        subscriber
            .Setup(value => value.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((channel, value, _) =>
            {
                publishedChannel = channel;
                publishedValue = value;
            })
            .ReturnsAsync(1);
        var bus = CreateBus(subscriber);
        var envelope = CreateEnvelope();

        await bus.PublishAsync(envelope);

        Assert.Equal("xians:cache:invalidate", publishedChannel.ToString());
        AssertEnvelopeMatches(
            envelope,
            JsonSerializer.Deserialize<CacheInvalidationEnvelope>(publishedValue.ToString()));
    }

    [Fact]
    public async Task PublishAsync_WhenCancellationRequested_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bus = CreateBus(new Mock<ISubscriber>());

        var exception = await Record.ExceptionAsync(() => bus.PublishAsync(CreateEnvelope(), cts.Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishAsync_WhenRedisFails_DoesNotThrow()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(value => value.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "offline"));
        var bus = CreateBus(subscriber);

        var exception = await Record.ExceptionAsync(() => bus.PublishAsync(CreateEnvelope()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_SubscriptionAppliesDeserializedEnvelope()
    {
        var subscriber = new Mock<ISubscriber>();
        Action<RedisChannel, RedisValue>? handler = null;
        subscriber
            .Setup(value => value.SubscribeAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<Action<RedisChannel, RedisValue>>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, callback, _) =>
                handler = callback)
            .Returns(Task.CompletedTask);
        var applicator = new Mock<ICacheInvalidationApplicator>();
        var bus = CreateBus(subscriber, applicator);
        var envelope = CreateEnvelope();

        await bus.StartAsync(CancellationToken.None);
        handler!(RedisChannel.Literal("xians:cache:invalidate"), JsonSerializer.Serialize(envelope));

        applicator.Verify(value => value.Apply(It.Is<CacheInvalidationEnvelope>(
            applied => EnvelopesMatch(envelope, applied))), Times.Once);
    }

    private static RedisCacheInvalidationBus CreateBus(
        Mock<ISubscriber> subscriber,
        Mock<ICacheInvalidationApplicator>? applicator = null)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(value => value.GetSubscriber(It.IsAny<object>()))
            .Returns(subscriber.Object);

        return new RedisCacheInvalidationBus(
            multiplexer.Object,
            (applicator ?? new Mock<ICacheInvalidationApplicator>()).Object,
            NullLogger<RedisCacheInvalidationBus>.Instance);
    }

    private static CacheInvalidationEnvelope CreateEnvelope() =>
        new(
            CacheInvalidationType.Tenant,
            UserId: null,
            TenantId: "tenant-1",
            Keys: ["tenant:byid:tenant-1"],
            PublishedAtUtc: DateTimeOffset.UnixEpoch);

    private static void AssertEnvelopeMatches(
        CacheInvalidationEnvelope expected,
        CacheInvalidationEnvelope? actual)
    {
        Assert.NotNull(actual);
        Assert.True(EnvelopesMatch(expected, actual));
    }

    private static bool EnvelopesMatch(
        CacheInvalidationEnvelope expected,
        CacheInvalidationEnvelope actual) =>
        expected.Type == actual.Type
        && expected.UserId == actual.UserId
        && expected.TenantId == actual.TenantId
        && (expected.Keys ?? []).SequenceEqual(actual.Keys ?? [])
        && expected.PublishedAtUtc == actual.PublishedAtUtc;
}
