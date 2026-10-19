using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class BackgroundTaskServiceShutdownTests
{
    private static BackgroundTaskService CreateService(TimeSpan drainTimeout) =>
        new(NullLogger<BackgroundTaskService>.Instance, drainTimeout);

    [Fact]
    public async Task StoppedAsync_DrainsWorkQueuedBeforeAndDuringShutdown()
    {
        var service = CreateService(TimeSpan.FromSeconds(10));
        await service.StartAsync(CancellationToken.None);

        var gate = new TaskCompletionSource();
        var completed = 0;
        service.QueueDatabaseOperation(async () => { await gate.Task; Interlocked.Increment(ref completed); });
        service.QueueDatabaseOperation(() => { Interlocked.Increment(ref completed); return Task.CompletedTask; });

        // Requests can still add work after StopAsync.
        await service.StopAsync(CancellationToken.None);
        service.QueueDatabaseOperation(() => { Interlocked.Increment(ref completed); return Task.CompletedTask; });

        gate.SetResult();
        await service.StoppedAsync(CancellationToken.None);

        Assert.Equal(3, completed);
        Assert.False(service.HasPendingTasks);
    }

    [Fact]
    public async Task StoppedAsync_AbandonsRemainingWork_WhenDrainTimesOut()
    {
        var service = CreateService(TimeSpan.FromMilliseconds(100));
        await service.StartAsync(CancellationToken.None);

        var blocker = new TaskCompletionSource();
        var secondRan = false;
        service.QueueDatabaseOperation(() => blocker.Task);
        service.QueueDatabaseOperation(() => { secondRan = true; return Task.CompletedTask; });

        using var hostShutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await service.StoppedAsync(hostShutdown.Token);

        blocker.SetResult();
        await Task.Delay(100);

        Assert.False(secondRan);
    }

    [Fact]
    public async Task HostShutdown_DrainsQueue_WithProductionRegistration()
    {
        // Same registration as SharedServices.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<BackgroundTaskService>();
        builder.Services.AddSingleton<IBackgroundTaskService>(sp => sp.GetRequiredService<BackgroundTaskService>());
        builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<BackgroundTaskService>());
        using var host = builder.Build();
        await host.StartAsync();

        var queue = host.Services.GetRequiredService<IBackgroundTaskService>();
        var completed = 0;
        for (var i = 0; i < 5; i++)
        {
            queue.QueueDatabaseOperation(async () => { await Task.Delay(50); Interlocked.Increment(ref completed); });
        }

        await host.StopAsync();

        Assert.Equal(5, completed);
    }

    [Fact]
    public async Task QueueDatabaseOperation_IsRejected_AfterShutdown()
    {
        var service = CreateService(TimeSpan.FromSeconds(1));
        await service.StartAsync(CancellationToken.None);
        await service.StoppedAsync(CancellationToken.None);

        var ran = false;
        service.QueueDatabaseOperation(() => { ran = true; return Task.CompletedTask; });
        await Task.Delay(50);

        Assert.False(ran);
        Assert.False(service.HasPendingTasks);
    }
}
