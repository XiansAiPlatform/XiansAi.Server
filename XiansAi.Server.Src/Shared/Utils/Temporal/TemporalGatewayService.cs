using Shared.Repositories;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Client;
using Temporalio.Extensions.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Utils.Temporal;

public interface ITemporalGatewayService
{
    IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId);
    Task<ITemporalClient> GetClientAsync(string tenantId, string? agentName = null);
    Task RemoveClients(string tenantId);
    Task EnsureSearchAttributesExistAsync(TemporalClient client, string @namespace = "default");
}

public class TemporalGatewayService : ITemporalGatewayService, IDisposable, IAsyncDisposable
{
    private readonly TemporalClientCache _clientCache = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed = false;
    private readonly object _disposeLock = new object();

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TemporalGatewayService> _logger;

    public TemporalGatewayService(
        IServiceScopeFactory serviceFactory,
        ILogger<TemporalGatewayService> logger,
        IConfiguration configuration)
    {
        _serviceScopeFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("TenantId is required");

        var clients = await GetOrConnectClientsForTenantAsync(tenantId);
        foreach (var client in clients)
        {
            yield return client;
        }
    }

    public async Task<ITemporalClient> GetClientAsync(string tenantId, string? agentName = null)
    {
        return await GetClientInternalAsync(tenantId, agentName);
    }

    private async Task<ITemporalClient> GetClientInternalAsync(string tenantId, string? agentName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("TenantId is required");

        await _connectionSemaphore.WaitAsync();
        try
        {
            var requestKey = TemporalClientCache.BuildRequestKey(tenantId, agentName);
            if (_clientCache.TryGet(requestKey, out var existingClient))
            {
                return existingClient;
            }

            var (config, configTenantId) = await GetTemporalConfig(tenantId, agentName);
            if (_clientCache.TryGetByConfigTenant(configTenantId, out existingClient))
            {
                _clientCache.Add(requestKey, configTenantId, existingClient);
                return existingClient;
            }

            var flowServerNamespace = config.FlowServerNamespace!;
            var options = new TemporalClientConnectOptions(new(config.FlowServerUrl))
            {
                Namespace = flowServerNamespace,
                Interceptors = [new TracingInterceptor()]
            };
            if (!string.IsNullOrEmpty(config.CertificateBase64) && !string.IsNullOrEmpty(config.PrivateKeyBase64))
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
            _clientCache.Add(requestKey, configTenantId, client);
            await EnsureSearchAttributesExistAsync(client, flowServerNamespace);
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

        IReadOnlyList<ITemporalClient> clientsToDispose;
        await _connectionSemaphore.WaitAsync();
        try
        {
            clientsToDispose = _clientCache.RemoveByTenant(tenantId);
            if (clientsToDispose.Count > 0)
            {
                _logger.LogInformation(
                    "Evicted {Count} Temporal client(s) for tenant {TenantId}",
                    clientsToDispose.Count, tenantId);
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }

        await DisposeClientsAsync(clientsToDispose, TimeSpan.FromSeconds(10));
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

    private async Task<(TemporalConfig Config, string ConfigTenantId)> GetTemporalConfig(string tenantId, string? agentName)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var tenantTemporalConfigRepository = scope.ServiceProvider.GetRequiredService<ITenantTemporalConfigRepository>();

        var configTenantId = tenantId;
        if (!string.IsNullOrEmpty(agentName))
        {
            var agent = await agentRepository.GetByNameAndOriginTenantAsync(agentName, tenantId);
            if (!string.IsNullOrWhiteSpace(agent?.OriginTenant))
            {
                configTenantId = agent.OriginTenant;
            }
        }

        var tenantConnection = await tenantTemporalConfigRepository.GetAsync(configTenantId);
        TemporalConfig? temporalConfig;
        if (tenantConnection != null)
        {
            temporalConfig = new TemporalConfig
            {
                FlowServerUrl = tenantConnection.ServerUrl,
                FlowServerNamespace = tenantConnection.Namespace,
                CertificateBase64 = tenantConnection.Certificate,
                PrivateKeyBase64 = tenantConnection.PrivateKey
            };
        }
        else
        {
            temporalConfig = _configuration.GetSection($"Tenants:{configTenantId}:Temporal").Get<TemporalConfig>()
                ?? _configuration.GetSection("Temporal").Get<TemporalConfig>();
        }

        if (temporalConfig == null)
        {
            throw new InvalidOperationException($"Temporal configuration for tenant {configTenantId} not found");
        }

        if (string.IsNullOrWhiteSpace(temporalConfig.FlowServerUrl))
            throw new InvalidOperationException($"FlowServerUrl is required for tenant {configTenantId}");

        if (string.IsNullOrWhiteSpace(temporalConfig.FlowServerNamespace))
            throw new InvalidOperationException($"FlowServerNamespace is required for tenant {configTenantId}");

        if (string.IsNullOrEmpty(temporalConfig.CertificateBase64) != string.IsNullOrEmpty(temporalConfig.PrivateKeyBase64))
        {
            throw new InvalidOperationException(
                $"Certificate and private key must both be set or both omitted for tenant {configTenantId}");
        }

        return (temporalConfig, configTenantId);
    }

    private async Task<List<ITemporalClient>> GetOrConnectClientsForTenantAsync(string tenantId)
    {
        var configTenantIds = await ResolveTemporalConfigTenantIdsAsync(tenantId);
        var uniqueClients = new List<ITemporalClient>();
        var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Exception? firstFailure = null;

        foreach (var configTenantId in configTenantIds)
        {
            ITemporalClient client;
            try
            {
                client = await GetClientInternalAsync(configTenantId, null);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                _logger.LogWarning(ex,
                    "Skipping Temporal cluster for config tenant {ConfigTenant} while listing clients for tenant {TenantId}",
                    configTenantId, tenantId);
                continue;
            }

            var identity = GetClientIdentity(client);
            if (seenIdentities.Add(identity))
            {
                uniqueClients.Add(client);
            }
        }

        if (uniqueClients.Count == 0)
        {
            throw firstFailure ?? new InvalidOperationException($"No Temporal clients available for tenant {tenantId}");
        }

        return uniqueClients;
    }

    private async Task<List<string>> ResolveTemporalConfigTenantIdsAsync(string tenantId)
    {
        var configTenantIds = new List<string> { tenantId };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { tenantId };

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
            var originTenants = await agentRepository.GetDistinctOriginTenantsAsync(tenantId) ?? new List<string>();
            foreach (var originTenant in originTenants)
            {
                if (seen.Add(originTenant))
                {
                    configTenantIds.Add(originTenant);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not list origin tenants for {TenantId} while resolving Temporal clients; using tenant config only",
                tenantId);
        }

        return configTenantIds;
    }

    private static string GetClientIdentity(ITemporalClient client)
    {
        var targetHost = client.Connection.Options.TargetHost ?? string.Empty;
        var @namespace = client.Options.Namespace ?? string.Empty;
        return $"{targetHost}|{@namespace}";
    }

    private async Task DisposeClientsAsync(IEnumerable<ITemporalClient> clients, TimeSpan timeout)
    {
        var disposeTasks = clients.Distinct().Select(async client =>
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
                _logger.LogError(ex, "Error disposing Temporal client");
            }
        });

        try
        {
            await Task.WhenAll(disposeTasks).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Timed out disposing Temporal clients after {TimeoutSeconds} seconds",
                timeout.TotalSeconds);
        }
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

        try
        {
            // A single client can be cached under several tenant ids, so dispose each instance once.
            await DisposeClientsAsync(_clientCache.GetDistinctClients(), disposeTimeout);

            _clientCache.Clear();

            _logger.LogInformation("Temporal client service disposed successfully");
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