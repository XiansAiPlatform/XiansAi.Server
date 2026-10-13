using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using Shared.Utils;
using Xunit;

namespace Tests.UnitTests.Shared.Utils;

public class MongoRetryHelperTests
{
    [Fact]
    public async Task ExecuteWithRetryAsync_AbandonsAnInFlightWait_WhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MongoRetryHelper.ExecuteWithRetryAsync(
                async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(60));
                    return 1;
                },
                NullLogger.Instance,
                maxRetries: 5,
                baseDelayMs: 2000,
                operationName: "Hang",
                cancellationToken: cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_DoesNotSitOutRetryBackoff_WhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        var attempts = 0;

        var retry = MongoRetryHelper.ExecuteWithRetryAsync<int>(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    cts.CancelAfter(TimeSpan.FromMilliseconds(50));
                }

                throw new TimeoutException("Mongo is gone");
            },
            NullLogger.Instance,
            maxRetries: 5,
            baseDelayMs: 2000,
            operationName: "TimeoutThenCancel",
            cancellationToken: cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        Assert.Equal(1, attempts);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ThrowsImmediately_WhenTokenIsAlreadyCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var called = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MongoRetryHelper.ExecuteWithRetryAsync(
                () =>
                {
                    called = true;
                    return Task.FromResult(1);
                },
                NullLogger.Instance,
                cancellationToken: cts.Token));

        Assert.False(called);
    }

    private static MongoWriteException DuplicateKey()
    {
        var writeError = (WriteError)Activator.CreateInstance(
            typeof(WriteError),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object?[] { ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key", new BsonDocument() },
            culture: null)!;
        var connectionId = new ConnectionId(new ServerId(new ClusterId(1), new DnsEndPoint("localhost", 27017)));
        return new MongoWriteException(connectionId, writeError, null, null);
    }

    private static void VerifyLogged(Mock<ILogger> logger, LogLevel level, Times times) =>
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    [Fact]
    public async Task ExecuteWithRetryAsync_DuplicateKey_IsRethrownWithoutErrorLog()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        await Assert.ThrowsAsync<MongoWriteException>(() =>
            MongoRetryHelper.ExecuteWithRetryAsync<int>(
                () => throw DuplicateKey(), logger.Object, operationName: "CreateKnowledge"));

        VerifyLogged(logger, LogLevel.Error, Times.Never());
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_UnexpectedException_StillLogsError()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MongoRetryHelper.ExecuteWithRetryAsync<int>(
                () => throw new InvalidOperationException("boom"), logger.Object, operationName: "CreateKnowledge"));

        VerifyLogged(logger, LogLevel.Error, Times.Once());
    }
}
