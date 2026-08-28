using System.Collections.Concurrent;
using Shared.Repositories;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;

namespace Shared.Utils.Temporal;

public interface ITemporalGatewayService
{
    IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId);
    Task<ITemporalClient> GetClientAsync(string tenantId, string agentName);
    Task<ITemporalClient> GetClientInternalAsync(string tenantId, string? agentName);
    Task RemoveClients(string tenantId);
    Task EnsureSearchAttributesExistAsync(TemporalClient client, string @namespace = "default");
}

public class TemporalGatewayService : ITemporalGatewayService, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ITemporalClient> _clients = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed = false;
    private readonly object _disposeLock = new object();

    private readonly IAgentRepository _agentRepository;
    private readonly ITenantTemporalConfigRepository _tenantTemporalConfigRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TemporalGatewayService> _logger;

    public TemporalGatewayService(
        IServiceScopeFactory serviceFactory,
        ILogger<TemporalGatewayService> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);
        using var scope = serviceFactory.CreateScope();
        _tenantTemporalConfigRepository = scope.ServiceProvider.GetRequiredService<ITenantTemporalConfigRepository>();
        _agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId)
    {
        ThrowIfDisposed();
        await _connectionSemaphore.WaitAsync();
        try
        {
            var clients = new List<ITemporalClient>();
            foreach (var client in _clients)
            {
                if (client.Key.StartsWith($"{tenantId}:"))
                {
                    yield return client.Value;
                }
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task<ITemporalClient> GetClientAsync(string tenantId, string agentName)
    {
        return await GetClientInternalAsync(tenantId, agentName);
    }

    public async Task<ITemporalClient> GetClientInternalAsync(string tenantId, string? agentName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("TenantId is required");

        await _connectionSemaphore.WaitAsync();
        try
        {
            String clientKey = string.IsNullOrEmpty(agentName) ? tenantId : $"{tenantId}:{agentName}";
            if (_clients.TryGetValue(clientKey, out var existingClient))
            {
                return existingClient;
            }

            var config = await GetTemporalConfig(tenantId, agentName);
            var options = new TemporalClientConnectOptions(new(config.FlowServerUrl))
            {
                Namespace = config.FlowServerNamespace!,
                Interceptors = [new TracingInterceptor()]
            };
            if (config.CertificateBase64 != null && config.PrivateKeyBase64 != null)
            {
                options.Tls = new TlsOptions()
                {
                    ClientCert = Convert.FromBase64String(config.CertificateBase64),
                    ClientPrivateKey = Convert.FromBase64String(config.PrivateKeyBase64)
                };
            }

            _logger.LogInformation("Connecting to temporal server for tenant {TenantId}: {Url}, namespace: {Namespace}",
                tenantId, config.FlowServerUrl, config.FlowServerNamespace);

            var client = await TemporalClient.ConnectAsync(options);
            _clients.TryAdd(clientKey, client);
            await EnsureSearchAttributesExistAsync(client, config.FlowServerNamespace!);
            _logger.LogInformation("Successfully connected to Temporal server for tenant {TenantId}", tenantId);
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Temporal server for tenant {TenantId} in agent {AgentName}. Error: {ErrorMessage}", tenantId, agentName, ex.Message);
            throw;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task RemoveClients(string tenantId)
    {
        ThrowIfDisposed();
        await _connectionSemaphore.WaitAsync();
        try
        {
            foreach (var client in _clients)
            {
                if (client.Key.StartsWith($"{tenantId}:"))
                {
                    _clients.TryRemove(client.Key, out _);
                }
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task EnsureSearchAttributesExistAsync(TemporalClient client, string @namespace = "default")
    {
        try
        {
            var existing = await client.Connection.OperatorService.ListSearchAttributesAsync(
                new ListSearchAttributesRequest { Namespace = @namespace });
            var existingNames = existing.CustomAttributes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = Constants.RequiredSearchAttributes
                .Where(attr => !existingNames.Contains(attr.Key))
                .ToList();

            if (missing.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Registering {Count} missing search attributes in namespace {Namespace}: {Attributes}",
                missing.Count, LogSanitizer.Sanitize(@namespace), string.Join(", ", missing.Select(a => a.Key)));

            var addRequest = new AddSearchAttributesRequest { Namespace = @namespace };
            foreach (var attr in missing)
            {
                addRequest.SearchAttributes.Add(attr.Key, attr.Value);
            }

            await client.Connection.OperatorService.AddSearchAttributesAsync(addRequest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not verify/register search attributes for namespace {Namespace}. " +
                "If workflows fail to start, manually register these attributes: {Attributes}",
                LogSanitizer.Sanitize(@namespace), string.Join(", ", Constants.RequiredSearchAttributes.Keys));
        }
    }
    private async Task<TemporalConfig> GetTemporalConfig(string tenantId, string? agentName)
    {

        if (!string.IsNullOrEmpty(agentName))
        {
            var agent = await _agentRepository.GetByNameAndOriginTenantAsync(agentName, tenantId);
            if (!string.IsNullOrEmpty(agent?.OriginTenant))
            {
                tenantId = agent.OriginTenant;
            }
        }
        var tenantConnection = await _tenantTemporalConfigRepository.GetAsync(tenantId);
        if (tenantConnection != null)
        {
            return new TemporalConfig
            {
                FlowServerUrl = tenantConnection.ServerUrl,
                FlowServerNamespace = tenantConnection.Namespace,
                CertificateBase64 = tenantConnection.Certificate == null ? null : tenantConnection.Certificate,
                PrivateKeyBase64 = tenantConnection.PrivateKey == null ? null : tenantConnection.PrivateKey
            };
        }

        // First try to get tenant-specific temporal config
        var temporalConfig = _configuration.GetSection($"Tenants:{tenantId}:Temporal").Get<TemporalConfig>();

        if (temporalConfig == null)
        {
            // Fallback to the root temporal config
            temporalConfig = _configuration.GetSection("Temporal").Get<TemporalConfig>();
        }

        // If neither tenant-specific nor default config is found, throw an error
        if (temporalConfig == null)
        {
            throw new InvalidOperationException($"Temporal configuration for tenant {tenantId} not found");
        }

        // Validate required fields
        if (temporalConfig.FlowServerUrl == null)
            throw new InvalidOperationException($"FlowServerUrl is required for tenant {tenantId}");

        return temporalConfig;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TemporalGatewayService));
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            lock (_disposeLock)
            {
                if (_disposed) return;

                _logger.LogInformation("Disposing Temporal client service synchronously");

                try
                {
                    // Use a timeout to prevent hanging during shutdown
                    var disposeTask = DisposeAsyncCore();
                    if (!disposeTask.AsTask().Wait(TimeSpan.FromSeconds(10)))
                    {
                        _logger.LogWarning("Temporal client service disposal timed out after 10 seconds");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during synchronous disposal of Temporal client service");
                }
                finally
                {
                    _disposed = true;
                    _connectionSemaphore?.Dispose();
                }
            }
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;

        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _logger.LogInformation("Disposing Temporal client service asynchronously");

        var disposeTimeout = TimeSpan.FromSeconds(10);
        var cancellationTokenSource = new CancellationTokenSource(disposeTimeout);

        try
        {
            // A single client can be cached under several tenant ids, so dispose each instance once.
            var disposeTasks = _clients.Values.Distinct().Select(async client =>
            {
                try
                {
                    if (client is IAsyncDisposable asyncDisposableClient)
                    {
                        await asyncDisposableClient.DisposeAsync();
                    }
                    else if (client is IDisposable disposableClient)
                    {
                        disposableClient.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing individual Temporal client");
                }
            });

            // Wait for all disposals to complete with timeout
            await Task.WhenAll(disposeTasks).WaitAsync(cancellationTokenSource.Token);

            _clients.Clear();

            _logger.LogInformation("Temporal client service disposed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Temporal client service disposal timed out after {TimeoutSeconds} seconds", disposeTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async disposal of Temporal client service");
        }
        finally
        {
            _connectionSemaphore?.Dispose();
        }
    }
}