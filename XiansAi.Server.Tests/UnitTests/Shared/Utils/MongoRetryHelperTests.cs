using Microsoft.Extensions.Logging.Abstractions;
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
}
