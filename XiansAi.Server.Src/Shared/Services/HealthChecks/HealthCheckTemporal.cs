using Shared.Utils.Temporal;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Workflows;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Connection to the platform-default Temporal namespace (the root <c>Temporal</c> configuration
/// section). Health-check schedules for every tenant and the server's health-check worker live
/// there, never in a tenant's own namespace: the server cannot run a worker against every tenant
/// cluster, and activation cleanup purges schedules in tenant namespaces by search attribute.
/// </summary>
public interface IHealthCheckTemporalClientProvider
{
    Task<TemporalClient> GetClientAsync(CancellationToken cancellationToken = default);
}

public sealed class HealthCheckTemporalClientProvider : IHealthCheckTemporalClientProvider, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthCheckTemporalClientProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TemporalClient? _client;

    public HealthCheckTemporalClientProvider(IConfiguration configuration, ILogger<HealthCheckTemporalClientProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TemporalClient> GetClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null)
        {
            return _client;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_client != null)
            {
                return _client;
            }

            var config = _configuration.GetSection("Temporal").Get<TemporalConfig>()
                ?? throw new InvalidOperationException("Platform Temporal configuration (section 'Temporal') is missing");
            if (string.IsNullOrWhiteSpace(config.FlowServerUrl) || string.IsNullOrWhiteSpace(config.FlowServerNamespace))
            {
                throw new InvalidOperationException("Temporal:FlowServerUrl and Temporal:FlowServerNamespace are required for health checks");
            }

            var options = new TemporalClientConnectOptions(config.FlowServerUrl)
            {
                Namespace = config.FlowServerNamespace,
                Interceptors = [new TracingInterceptor()]
            };
            if (!string.IsNullOrEmpty(config.CertificateBase64) && !string.IsNullOrEmpty(config.PrivateKeyBase64))
            {
                options.Tls = new TlsOptions
                {
                    ClientCert = Convert.FromBase64String(config.CertificateBase64),
                    ClientPrivateKey = Convert.FromBase64String(config.PrivateKeyBase64)
                };
            }

            _logger.LogInformation("Connecting health-check Temporal client to {Url}, namespace {Namespace}",
                config.FlowServerUrl, config.FlowServerNamespace);
            _client = await TemporalClient.ConnectAsync(options);
            return _client;
        }
        finally
        {
            _lock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Started by each check's Temporal Schedule. Deliberately thin: all side effects live in the
/// single activity, which runs inside the server process with full DI access.
/// </summary>
[Workflow(Name)]
public class AgentHealthCheckWorkflow
{
    public const string Name = "Xians.AgentHealthCheck";

    /// <summary>Upper bound for one run: the longest check timeout (120s) plus executor and persistence slack.</summary>
    public static readonly TimeSpan ActivityTimeout = TimeSpan.FromMinutes(5);

    [WorkflowRun]
    public Task<string> RunAsync(string tenantId, string checkId) =>
        Workflow.ExecuteActivityAsync(
            (HealthCheckActivities activities) => activities.RunHealthCheckAsync(tenantId, checkId),
            new ActivityOptions
            {
                StartToCloseTimeout = ActivityTimeout,
                // A failing check is a result to record, not an error to retry; retries would mask
                // flakiness and repeat the agent's (LLM) work.
                RetryPolicy = new RetryPolicy { MaximumAttempts = 1 }
            });
}

public class HealthCheckActivities
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HealthCheckActivities(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    [Activity("Xians.RunAgentHealthCheck")]
    public async Task<string> RunHealthCheckAsync(string tenantId, string checkId)
    {
        var context = ActivityExecutionContext.Current;

        using var scope = _scopeFactory.CreateScope();
        HealthCheckSystemPrincipal.Apply(scope.ServiceProvider.GetRequiredService<Shared.Auth.ITenantContext>(), tenantId);

        var runner = scope.ServiceProvider.GetRequiredService<IHealthCheckRunner>();
        var outcome = await runner.RunAsync(tenantId, checkId, context.Info.WorkflowRunId, context.CancellationToken);
        return outcome.ToString();
    }
}
