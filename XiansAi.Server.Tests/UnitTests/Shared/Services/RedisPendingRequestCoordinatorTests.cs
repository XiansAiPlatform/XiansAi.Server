using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Repositories;
using Shared.Services;
using StackExchange.Redis;

namespace Tests.UnitTests.Shared.Services;

public class RedisPendingRequestCoordinatorTests
{
    private const string PlaintextMarker = "SECRET_CHAT_CONTENT";

    [Fact]
    public async Task PublishCompletionAsync_StoresEncryptedResultThenPublishesSignal()
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
        var encryption = CreatePassThroughEncryption();
        var coordinator = CreateCoordinator(database, subscriber, encryption.Object);
        var response = CreateMessage("req-1", PlaintextMarker);

        await coordinator.PublishCompletionAsync("req-1", response, MessageType.Chat);

        Assert.Equal("xians:pending:result:req-1", storedKey.ToString());
        Assert.Equal(TimeSpan.FromSeconds(300), storedExpiry);
        Assert.DoesNotContain(PlaintextMarker, storedValue.ToString());
        Assert.DoesNotContain("\"Response\"", storedValue.ToString());
        Assert.StartsWith("enc:", storedValue.ToString());
        Assert.Equal("xians:pending:complete", publishedChannel.ToString());
        using var signal = JsonDocument.Parse(publishedSignal.ToString());
        Assert.Equal("req-1", signal.RootElement.GetProperty("RequestId").GetString());
        encryption.Verify(value => value.Encrypt(
            It.Is<string>(payload => payload.Contains(PlaintextMarker)),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task PublishCompletionAsync_WhenEncryptFails_DoesNotWriteToRedis()
    {
        var database = new Mock<IDatabase>();
        var encryption = new Mock<ISecureEncryptionService>();
        encryption
            .Setup(value => value.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new ArgumentException("uniqueSecret must be provided"));
        var coordinator = CreateCoordinator(database, new Mock<ISubscriber>(), encryption.Object);

        var exception = await Record.ExceptionAsync(() =>
            coordinator.PublishCompletionAsync("req-1", CreateMessage("req-1"), MessageType.Chat));

        Assert.Null(exception);
        database.Verify(value => value.StringSetAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task AnnounceWaitAsync_WhenEncryptedResultExists_NotifiesLocalListener()
    {
        var response = CreateMessage("req-1", PlaintextMarker);
        var plaintext = JsonSerializer.Serialize(new
        {
            Response = response,
            MessageType = MessageType.Chat
        });
        var ciphertext = "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        var database = new Mock<IDatabase>();
        database
            .Setup(value => value.StringGetAsync(
                "xians:pending:result:req-1",
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(ciphertext);
        var coordinator = CreateCoordinator(database, new Mock<ISubscriber>(), CreatePassThroughEncryption().Object);
        ConversationMessage? received = null;
        coordinator.CompletionReceived += (_, message, _) => received = message;

        await coordinator.AnnounceWaitAsync("req-1");

        Assert.NotNull(received);
        Assert.Equal("req-1", received.RequestId);
        Assert.Equal(PlaintextMarker, received.Text);
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
        var coordinator = CreateCoordinator(database, new Mock<ISubscriber>(), CreatePassThroughEncryption().Object);
        using var cts = new CancellationTokenSource();
        var announceTask = coordinator.AnnounceWaitAsync("req-1", cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => announceTask);
    }

    private static Mock<ISecureEncryptionService> CreatePassThroughEncryption()
    {
        var encryption = new Mock<ISecureEncryptionService>();
        encryption
            .Setup(value => value.Encrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string plaintext, string _) =>
                "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)));
        encryption
            .Setup(value => value.Decrypt(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string ciphertext, string _) =>
            {
                var encoded = ciphertext.StartsWith("enc:", StringComparison.Ordinal)
                    ? ciphertext["enc:".Length..]
                    : ciphertext;
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            });
        return encryption;
    }

    private static RedisPendingRequestCoordinator CreateCoordinator(
        Mock<IDatabase> database,
        Mock<ISubscriber> subscriber,
        ISecureEncryptionService encryption)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(value => value.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        multiplexer
            .Setup(value => value.GetSubscriber(It.IsAny<object>()))
            .Returns(subscriber.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:BaseSecret"] = "test-base-secret",
                ["EncryptionKeys:UniqueSecrets:ConversationMessageKey"] = "test-conversation-secret"
            })
            .Build();

        return new RedisPendingRequestCoordinator(
            new Lazy<IConnectionMultiplexer>(() => multiplexer.Object),
            encryption,
            configuration,
            NullLogger<RedisPendingRequestCoordinator>.Instance);
    }

    private static ConversationMessage CreateMessage(string requestId, string? text = null) =>
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
            WorkflowType = "test",
            Text = text
        };
}
