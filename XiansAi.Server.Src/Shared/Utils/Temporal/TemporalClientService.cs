using Temporalio.Client;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Extensions.OpenTelemetry;
using System.Collections.Concurrent;
using Shared.Utils;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shared.Services;
using Temporalio.Api.WorkflowService.V1;
using Google.Protobuf.WellKnownTypes;
using Temporalio.Exceptions;

namespace Shared.Utils.Temporal;

public interface ITemporalClientService
{
    Task<ITemporalClient> GetClientAsync(string tenantId);
    ITemporalClient GetClient(string tenantId);
    Task ProvisionTenantNamespaceAsync(string tenantId, string createdBy);

}

public class TemporalClientService : ITemporalClientService, IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ITemporalClient> _clients = new();
    private readonly ConcurrentDictionary<string, CloudService> _serviceClients = new();
    private readonly ConcurrentDictionary<string, bool> _searchAttributesRegistered = new();
    // private readonly ITenantCacheService _tenantCacheService;
    private readonly ITenantTemporalConfigService _tenantTemporalConfigService;
    private readonly ILogger<TemporalClientService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private volatile bool _disposed = false;
    private readonly object _disposeLock = new object();
    private const int CaValidityDays = 825;
    private const int LeafValidityDays = 825;

    public TemporalClientService(
        ILogger<TemporalClientService> logger,
        IConfiguration configuration,
        // ITenantCacheService tenantCacheService,
        ITenantTemporalConfigService tenantTemporalConfigService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        // _tenantCacheService = tenantCacheService ?? throw new ArgumentNullException(nameof(tenantCacheService));
        _tenantTemporalConfigService = tenantTemporalConfigService ?? throw new ArgumentNullException(nameof(tenantTemporalConfigService));
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

            return await GetClientAsync(tenantId, config);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    private async Task<ITemporalClient> GetClientAsync(string tenantId, TemporalConfig config)
    {
        var options = new TemporalClientConnectOptions(new(config.FlowServerUrl))
        {
            Namespace = config.FlowServerNamespace!,
            // Propagates OpenTelemetry trace context into Temporal workflows automatically.
            // No-op when no TracerProvider is configured (safe to keep always enabled).
            Interceptors = [new TracingInterceptor()]
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

        try
        {
            var client = await TemporalClient.ConnectAsync(options);
            _clients.TryAdd(tenantId, client);

            await EnsureSearchAttributesRegisteredAsync(client, config.FlowServerNamespace!);

            _logger.LogInformation("Successfully connected to Temporal server for tenant {TenantId}", tenantId);
            return client;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Temporal server for tenant {TenantId} at {Url}. Error: {ErrorMessage}",
                tenantId, config.FlowServerUrl, ex.Message);
            _clients.TryRemove(tenantId, out _);
            throw;
        }
    }

    public async Task ProvisionTenantNamespaceAsync(string tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));

        string namespaceName = $"tenant-{tenantId}";
        using var tenantCaKey = RSA.Create(4096);
        var caRequest = new CertificateRequest(
            $"CN=tenant-ca-{tenantId}",
            tenantCaKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        caRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        var caNotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var caNotAfter = DateTimeOffset.UtcNow.AddDays(CaValidityDays);
        using var tenantCaCert = caRequest.CreateSelfSigned(caNotBefore, caNotAfter);

        string rootCertificate = tenantCaCert.ExportCertificatePem();
        string rootCertificateKey = tenantCaKey.ExportPkcs8PrivateKeyPem();

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            $"CN={namespaceName}",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        leafRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        leafRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") /* clientAuth */ }, critical: false));

        var leafNotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var leafNotAfter = DateTimeOffset.UtcNow.AddDays(LeafValidityDays);
        if (leafNotAfter > tenantCaCert.NotAfter)
        {
            leafNotAfter = tenantCaCert.NotAfter;
        }
        using var signedLeaf = leafRequest.Create(tenantCaCert, leafNotBefore, leafNotAfter, RandomNumberGenerator.GetBytes(16));

        string leafCertificate = signedLeaf.ExportCertificatePem();
        string leafCertificateKey = leafKey.ExportPkcs8PrivateKeyPem();

        await _tenantTemporalConfigService.CreateAsync(tenantId, rootCertificate, rootCertificateKey, leafCertificate, leafCertificateKey, createdBy);


        var createReq = new RegisterNamespaceRequest
        {
            Namespace = namespaceName,
            Description = $"programmatically provisioning a separate, dedicated namespace for each tenant {tenantId}.",
            WorkflowExecutionRetentionPeriod = Duration.FromTimeSpan(TimeSpan.FromDays(1)),
        };
        TemporalConfig? defaultTemporalConfig = _configuration.GetSection("Temporal").Get<TemporalConfig>();
        if (defaultTemporalConfig == null)
            throw new InvalidOperationException($"Temporal configuration for tenant {tenantId} not found");

        if (defaultTemporalConfig.FlowServerUrl == null)
            throw new InvalidOperationException($"FlowServerUrl is required for tenant {tenantId}");

        ITemporalClient client = await GetClientAsync(tenantId, defaultTemporalConfig);
        try
        {
            await client.WorkflowService.RegisterNamespaceAsync(createReq, default);
            _logger.LogInformation("Temporal namespace {Namespace} registered for tenant {TenantId}", namespaceName, tenantId);
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.AlreadyExists)
        {
            throw new InvalidOperationException($"Temporal namespace {namespaceName} already exists for tenant {tenantId}");
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

    /// <summary>
    /// Ensures that required search attributes are registered in Temporal.
    /// Called once per namespace when a client connects.
    /// </summary>
    private async Task EnsureSearchAttributesRegisteredAsync(ITemporalClient client, string namespaceName)
    {
        if (_searchAttributesRegistered.TryGetValue(namespaceName, out var registered) && registered)
            return;

        try
        {
            _logger.LogInformation("Checking search attributes for namespace {Namespace}", namespaceName);

            var listRequest = new ListSearchAttributesRequest { Namespace = namespaceName };
            var existingAttributes = await client.Connection.OperatorService.ListSearchAttributesAsync(listRequest);
            var existingNames = existingAttributes.CustomAttributes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingAttributes = Constants.RequiredSearchAttributes
                .Where(attr => !existingNames.Contains(attr.Key))
                .ToList();

            if (missingAttributes.Count == 0)
            {
                _logger.LogInformation("All required search attributes already exist in namespace {Namespace}", namespaceName);
                _searchAttributesRegistered[namespaceName] = true;
                return;
            }

            _logger.LogInformation("Registering {Count} missing search attributes in namespace {Namespace}: {Attributes}",
                missingAttributes.Count, namespaceName, string.Join(", ", missingAttributes.Select(a => a.Key)));

            var addRequest = new AddSearchAttributesRequest { Namespace = namespaceName };
            foreach (var attr in missingAttributes)
                addRequest.SearchAttributes.Add(attr.Key, attr.Value);

            await client.Connection.OperatorService.AddSearchAttributesAsync(addRequest);
            _logger.LogInformation("Successfully registered search attributes in namespace {Namespace}", namespaceName);
            _searchAttributesRegistered[namespaceName] = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not auto-register search attributes for namespace {Namespace}. " +
                "If workflows fail to start, manually register these attributes: {Attributes}",
                namespaceName, string.Join(", ", Constants.RequiredSearchAttributes.Keys));
            _searchAttributesRegistered[namespaceName] = true;
        }
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