using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Workflows;
using Xians.Lib.Common.Caching;
using Xians.Lib.Common.Testing;
using Xians.Lib.Configuration.Models;

namespace Tests.TestUtils;

/// <summary>
/// Shared host for Admin API tests that author a real agent with Xians.Lib.
/// Owns the TestServer loopback, platform, worker tasks, and Lib static reset.
/// Register as many templates as the cycle needs, then <see cref="StartWorkersAsync"/>.
/// </summary>
public sealed class LibAgentWorkflowHost : IAsyncDisposable
{
    private readonly TestServerLoopback _loopback;
    private readonly List<(CancellationTokenSource Cts, Task Task)> _workers = [];
    private bool _disposed;

    private LibAgentWorkflowHost(TestServerLoopback loopback, XiansPlatform platform)
    {
        _loopback = loopback;
        Platform = platform;
    }

    public XiansPlatform Platform { get; }

    public static async Task<LibAgentWorkflowHost> StartAsync(
        TestServer server,
        string temporalHost,
        string temporalNamespace,
        string certificateTenantId,
        string certificateUserId)
    {
        TestCleanup.ResetAllStaticState();
        WorkflowDefinitionUploader.ResetCache();

        var loopback = TestServerLoopback.Start(server);
        var platform = await XiansPlatform.InitializeAsync(new XiansOptions
        {
            ServerUrl = loopback.BaseAddress,
            ApiKey = XiansLibTestCertificate.CreateApiKey(certificateTenantId, certificateUserId),
            ConsoleLogLevel = LogLevel.Warning,
            ServerLogLevel = LogLevel.None,
            EnableTasks = false,
            Cache = new CacheOptions { Enabled = false },
            TemporalConfiguration = new TemporalConfiguration
            {
                ServerUrl = temporalHost,
                Namespace = temporalNamespace
            }
        });

        return new LibAgentWorkflowHost(loopback, platform);
    }

    public XiansAgent RegisterTemplate(
        string agentName,
        string? description = null,
        IReadOnlyList<string>? samplePrompts = null)
    {
        return Platform.Agents.Register(new XiansAgentRegistration
        {
            Name = agentName,
            Description = description ?? agentName,
            SamplePrompts = samplePrompts,
            IsTemplate = true,
            EnableTasks = false
        });
    }

    public async Task StartWorkersAsync(params XiansAgent[] agents)
    {
        foreach (var agent in agents)
        {
            var cts = new CancellationTokenSource();
            var workerTask = agent.RunAllAsync(cts.Token);
            await WaitUntilRunningAsync(workerTask);
            _workers.Add((cts, workerTask));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var (cts, workerTask) in _workers)
        {
            cts.Cancel();
            try
            {
                await workerTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // Worker shutdown is best-effort; Temporal cancel races are expected.
            }

            cts.Dispose();
        }

        TestCleanup.ResetAllStaticState();
        WorkflowDefinitionUploader.ResetCache();
        await _loopback.DisposeAsync();
    }

    private static async Task WaitUntilRunningAsync(Task workerTask)
    {
        var started = await Task.WhenAny(workerTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (started == workerTask)
        {
            await workerTask;
        }
    }
}
