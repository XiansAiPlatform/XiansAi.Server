using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Repositories;
using Shared.Services;
using StackExchange.Redis;

namespace Tests.UnitTests.Shared.Services;

public class RedisPendingRequestCoordinatorTests
{
    [Fact]
    public async Task PublishCompletionAsync_StoresResultThenPublishesSignal()
    {
        var database = new Mock<IDatabase>();
        var subscriber = new Mock<ISubscriber>();
        RedisKey storedKey = default;
        RedisValue storedValue = default;
        TimeSpan? storedExpiry = null;
        database
            .Setup(value => value.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<When>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, TimeSpan?, When, CommandFlags>(
                (key, value, expiry, _, _) =>
                {
                    storedKey = key;
                    storedValue = value;
                    storedExpiry = expiry;
                })
            .ReturnsAsync(true);
        RedisChannel publishedChannel = default;
        RedisValue publishedSignal = default;
        subscriber
            .Setup(value => value.PublishAsync(
                It.IsAny<RedisChannel>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisChannel, RedisValue, CommandFlags>((channel, signal, _) =>
            {
                publishedChannel = channel;
                publishedSignal = signal;
            })
            .ReturnsAsync(1);
        var coordinator = CreateCoordinator(database, subscriber);
        var response = CreateMessage("req-1");

        await coordinator.PublishCompletionAsync("req-1", response, MessageType.Chat);

        Assert.Equal("xians:pending:result:req-1", storedKey.ToString());
        Assert.Equal(TimeSpan.FromSeconds(300), storedExpiry);
        Assert.Contains("\"Response\"", storedValue.ToString());
        Assert.Equal("xians:pending:complete", publishedChannel.ToString());
        using var signal = JsonDocument.Parse(publishedSignal.ToString());
        Assert.Equal("req-1", signal.RootElement.GetProperty("RequestId").GetString());
    }

    [Fact]
    public async Task AnnounceWaitAsync_WhenResultExists_NotifiesLocalListener()
    {
        var response = CreateMessage("req-1");
        var payload = JsonSerializer.Serialize(new
        {
            Response = response,
            MessageType = MessageType.Chat
        });
        var database = new Mock<IDatabase>();
        database
            .Setup(value => value.StringGetAsync(
                "xians:pending:result:req-1",
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(payload);
        var coordinator = CreateCoordinator(database, new Mock<ISubscriber>());
        ConversationMessage? received = null;
        coordinator.CompletionReceived += (_, message, _) => received = message;

        await coordinator.AnnounceWaitAsync("req-1");

        Assert.NotNull(received);
        Assert.Equal("req-1", received.RequestId);
    }

    [Fact]
    public async Task AnnounceWaitAsync_WhenCancelledDuringRedisGet_ThrowsOperationCanceledException()
    {
        var database = new Mock<IDatabase>();
        var getGate = new TaskCompletionSource<RedisValue>();
        database
            .Setup(value => value.StringGetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<CommandFlags>()))
            .Returns(getGate.Task);
        var coordinator = CreateCoordinator(database, new Mock<ISubscriber>());
        using var cts = new CancellationTokenSource();
        var announceTask = coordinator.AnnounceWaitAsync("req-1", cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => announceTask);
    }

    private static RedisPendingRequestCoordinator CreateCoordinator(
        Mock<IDatabase> database,
        Mock<ISubscriber> subscriber)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(value => value.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        multiplexer
            .Setup(value => value.GetSubscriber(It.IsAny<object>()))
            .Returns(subscriber.Object);

        return new RedisPendingRequestCoordinator(
            multiplexer.Object,
            NullLogger<RedisPendingRequestCoordinator>.Instance);
    }

    private static ConversationMessage CreateMessage(string requestId) =>
        new()
        {
            RequestId = requestId,
            MessageType = MessageType.Chat,
            ThreadId = "thread-1",
            TenantId = "tenant-1",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Direction = MessageDirection.Outgoing,
            ParticipantId = "participant-1",
            WorkflowId = "workflow-1",
            WorkflowType = "test"
        };
}
