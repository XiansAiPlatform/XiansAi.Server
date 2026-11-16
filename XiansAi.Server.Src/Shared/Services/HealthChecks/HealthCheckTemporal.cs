using Shared.Utils.Temporal;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Workflows;

namespace Shared.Services.HealthChecks;

/// <summary>
/// The connection to the platform's main Temporal namespace.
/// All health-check schedules and the server's health-check worker use this namespace,
/// not the tenants' own namespaces.
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
/// Started by each check's schedule. It only calls one activity, which does all the work.
/// </summary>
[Workflow(Name)]
public class AgentHealthCheckWorkflow
{
    public const string Name = "Xians.AgentHealthCheck";

    /// <summary>The most time one run can take: the longest timeout (120 seconds) plus some extra time.</summary>
    public static readonly TimeSpan ActivityTimeout = TimeSpan.FromMinutes(5);

    [WorkflowRun]
    public Task<string> RunAsync(string tenantId, string checkId) =>
        Workflow.ExecuteActivityAsync(
            (HealthCheckActivities activities) => activities.RunHealthCheckAsync(tenantId, checkId),
            new ActivityOptions
            {
                StartToCloseTimeout = ActivityTimeout,
                // Don't retry. A failed check is a result to save, and a retry would call the agent again.
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
