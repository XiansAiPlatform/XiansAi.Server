using Temporalio.Client;
using System.Collections.Concurrent;
using Temporalio.Api.Cloud.CloudService.V1;
using Temporalio.Api.Cloud.Namespace.V1;
using Temporalio.Api.Cloud.Identity.V1;
using Google.Protobuf.WellKnownTypes;
using Shared.Auth;

namespace Shared.Utils.Temporal;

public interface ITemporalClientService
{
    Task<ITemporalClient> GetClientAsync(string tenantId);
    ITemporalClient GetClient(string tenantId);

}

public class TemporalClientService : ITemporalClientService, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ITemporalClient> _clients = new();
    private readonly ConcurrentDictionary<string, CloudService> _serviceClients = new();
    private readonly ILogger<TemporalClientService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed = false;
    private readonly object _disposeLock = new object();

    public TemporalClientService(
        ILogger<TemporalClientService> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public ITemporalClient GetClient(string tenantId)
    {
        // For backward compatibility, use async version but block
        // Consider making all callers async to avoid this
        return GetClientAsync(tenantId).GetAwaiter().GetResult();
    }

    public async Task<ITemporalClient> GetClientAsync(string tenantId)
    {
        ThrowIfDisposed();
        
        if (_clients.TryGetValue(tenantId, out var existingClient))
        {
            return existingClient;
        }

        // Use semaphore to prevent concurrent creation of the same client
        await _connectionSemaphore.WaitAsync();
        try
        {
            // Double-check pattern
            if (_clients.TryGetValue(tenantId, out existingClient))
            {
                return existingClient;
            }

            var config = GetTemporalConfig(tenantId);
            
            var options = new TemporalClientConnectOptions(new(config.FlowServerUrl))
            {
                Namespace = config.FlowServerNamespace!,
            };
            
            if (config.CertificateBase64 != null && config.PrivateKeyBase64 != null) 
            {
                options.Tls = new TlsOptions()
                {
                    ClientCert = GetCertificate(config),
                    ClientPrivateKey = GetPrivateKey(config),
                };
            }
            
            _logger.LogInformation("Connecting to temporal server for tenant {TenantId}: {Url}, namespace: {Namespace}", 
                tenantId, config.FlowServerUrl, config.FlowServerNamespace);

            var client = await TemporalClient.ConnectAsync(options);
            _clients.TryAdd(tenantId, client);
            
            return client;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private TemporalConfig GetTemporalConfig(string tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("TenantId is required");

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

    private byte[]? GetCertificate(TemporalConfig config)
    {
        if (config.CertificateBase64 == null) 
        {
            return null;
        }
        return Convert.FromBase64String(config.CertificateBase64);
    }

    private byte[]? GetPrivateKey(TemporalConfig config)
    {
        if (config.PrivateKeyBase64 == null) 
        {
            return null;
        }
        return Convert.FromBase64String(config.PrivateKeyBase64);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TemporalClientService));
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
            // Dispose all cached clients with timeout
            var disposeTasks = _clients.Values.Select(async client =>
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
            _serviceClients.Clear();
            
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