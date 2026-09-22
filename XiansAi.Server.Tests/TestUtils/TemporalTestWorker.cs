using Shared.Services;
using Temporalio.Client;
using Temporalio.Worker;

namespace Tests.TestUtils;

/// <summary>
/// Runs a <see cref="StubAgentWorkflow"/> worker against the local Temporal CLI
/// in the test process so Admin API HTTP calls can complete signals and queries.
/// </summary>
public sealed class TemporalTestWorker : IAsyncDisposable
{
    private readonly TemporalWorker _worker;
    private readonly CancellationTokenSource _cts;
    private readonly Task _run;

    private TemporalTestWorker(TemporalWorker worker, CancellationTokenSource cts, Task run)
    {
        _worker = worker;
        _cts = cts;
        _run = run;
    }

    public static async Task<TemporalTestWorker> StartAsync(
        ITemporalClient client,
        string taskQueue,
        IPendingRequestService pendingRequests,
        WorkerDeploymentOptions? deploymentOptions = null)
    {
        var options = new TemporalWorkerOptions(taskQueue);
        options.AddWorkflow<StubAgentWorkflow>();
        options.AddAllActivities(new StubReplyActivities(pendingRequests));
        if (deploymentOptions != null)
        {
            options.DeploymentOptions = deploymentOptions;
        }

        var worker = new TemporalWorker(client, options);
        var cts = new CancellationTokenSource();
        var run = worker.ExecuteAsync(cts.Token);

        var first = await Task.WhenAny(run, Task.Delay(TimeSpan.FromMilliseconds(400)));
        if (first == run)
        {
            await run;
        }

        return new TemporalTestWorker(worker, cts, run);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _run;
        }
        catch (OperationCanceledException)
        {
        }

        _worker.Dispose();
        _cts.Dispose();
    }
}
