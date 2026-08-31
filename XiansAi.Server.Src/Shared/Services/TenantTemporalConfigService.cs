using Google.Protobuf.WellKnownTypes;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Api.WorkflowService.V1;
using Temporalio.Client;
using Temporalio.Exceptions;

namespace Shared.Services;

public class UpsertTenantTemporalConfigRequest
{
    public required string TenantId { get; set; }
    public required string ServerUrl { get; set; }
    public required string Namespace { get; set; }
    public string? Certificate { get; set; }
    public string? PrivateKey { get; set; }
}

public interface ITenantTemporalConfigService
{
    Task<ServiceResult<UpsertTenantTemporalConfigRequest?>> GetForTenantAsync(string tenantId);
    Task<ServiceResult<bool>> UpsertAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey, string actor);
    Task<ServiceResult<bool>> RevertAsync(string tenantId, string actor);
    Task<ServiceResult<bool>> CheckConnectivityAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey);
}

public class TenantTemporalConfigService : ITenantTemporalConfigService
{
    private readonly ILogger<TenantTemporalConfigService> _logger;
    private readonly ITenantTemporalConfigRepository _repository;
    private readonly ITemporalGatewayService _temporalGatewayService;

    public TenantTemporalConfigService(
        ITenantTemporalConfigRepository repository,
        ITemporalGatewayService temporalGatewayService,
        ILogger<TenantTemporalConfigService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _temporalGatewayService = temporalGatewayService ?? throw new ArgumentNullException(nameof(temporalGatewayService));
    }

    public async Task<ServiceResult<UpsertTenantTemporalConfigRequest?>> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<UpsertTenantTemporalConfigRequest?>.BadRequest("tenantId is required");

        try
        {
            // Repository only returns the active (non-deleted) row, already decrypted.
            var doc = await _repository.GetAsync(tenantId);
            if (doc == null) return ServiceResult<UpsertTenantTemporalConfigRequest?>.Success(null);

            var config = new UpsertTenantTemporalConfigRequest
            {
                TenantId = doc.TenantId,
                ServerUrl = doc.ServerUrl,
                Namespace = doc.Namespace,
                Certificate = doc.Certificate,
                PrivateKey = doc.PrivateKey
            };
            return ServiceResult<UpsertTenantTemporalConfigRequest?>.Success(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<UpsertTenantTemporalConfigRequest?>.InternalServerError("Failed to load Temporal configuration");
        }
    }

    public async Task<ServiceResult<bool>> UpsertAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey, string actor)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<bool>.BadRequest("tenantId is required");

        try
        {
            var connectivityResult = await CheckConnectivityAsync(tenantId, serverUrl, @namespace, certificate, privateKey);
            if (!connectivityResult.IsSuccess)
            {
                return connectivityResult;
            }

            // If certificate and privateKey are not provided, try to retrieve them from the repository for the given tenantId
            if (string.IsNullOrEmpty(certificate) && string.IsNullOrEmpty(privateKey))
            {
                var tenantConfig = await _repository.GetAsync(tenantId, serverUrl);
                if (tenantConfig != null)
                {
                    certificate = tenantConfig.Certificate;
                    privateKey = tenantConfig.PrivateKey;
                }
            }

            await _repository.UpsertAsync(tenantId, serverUrl, @namespace, certificate, privateKey, actor);
            await _temporalGatewayService.RemoveClients(tenantId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Failed to save Temporal configuration");
        }
    }

    public async Task<ServiceResult<bool>> RevertAsync(string tenantId, string actor)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<bool>.BadRequest("tenantId is required");

        try
        {
            var reverted = await _repository.RevertAsync(tenantId, actor);
            await _temporalGatewayService.RemoveClients(tenantId);
            return reverted ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound("No configuration found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Failed to revert Temporal configuration");
        }
    }

    public async Task<ServiceResult<bool>> CheckConnectivityAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return ServiceResult<bool>.BadRequest("serverUrl is required");
        if (string.IsNullOrWhiteSpace(@namespace))
            return ServiceResult<bool>.BadRequest("namespace is required");
        if (string.IsNullOrEmpty(certificate) != string.IsNullOrEmpty(privateKey))
            return ServiceResult<bool>.BadRequest("certificate and privateKey must be provided together");

        TemporalClient? client = null;
        try
        {
            // If certificate and privateKey are not provided, try to retrieve them from the repository for the given tenantId
            if (string.IsNullOrEmpty(certificate) && string.IsNullOrEmpty(privateKey))
            {
                var tenantConfig = await _repository.GetAsync(tenantId, serverUrl);
                if (tenantConfig != null)
                {
                    certificate = tenantConfig.Certificate;
                    privateKey = tenantConfig.PrivateKey;
                }
            }
            client = await ConnectWithoutNamespaceAsync(serverUrl, certificate, privateKey);
            await EnsureNamespaceExistsAsync(client, serverUrl, @namespace);
            await _temporalGatewayService.EnsureSearchAttributesExistAsync(client, @namespace);
            return ServiceResult<bool>.Success(true);
        }
        catch (FormatException ex)
        {
            return ServiceResult<bool>.BadRequest($"certificate/privateKey is not valid base64: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Temporal connectivity check failed for {ServerUrl}/{Namespace}",
                LogSanitizer.Sanitize(serverUrl), LogSanitizer.Sanitize(@namespace));
            return ServiceResult<bool>.BadRequest($"Could not connect to Temporal: {ex.Message}");
        }
        finally
        {
            await DisposeAsync(client);
        }
    }

    private static async Task<TemporalClient> ConnectWithoutNamespaceAsync(string serverUrl, string? certificate, string? privateKey)
    {
        var options = new TemporalClientConnectOptions(new(serverUrl));

        if (!string.IsNullOrEmpty(certificate) && !string.IsNullOrEmpty(privateKey))
        {
            options.Tls = new TlsOptions
            {
                ClientCert = Convert.FromBase64String(certificate),
                ClientPrivateKey = Convert.FromBase64String(privateKey)
            };
        }

        return await TemporalClient.ConnectAsync(options);
    }

    private async Task EnsureNamespaceExistsAsync(TemporalClient client, string serverUrl, string @namespace)
    {
        try
        {
            await client.WorkflowService.DescribeNamespaceAsync(new DescribeNamespaceRequest { Namespace = @namespace });
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            _logger.LogInformation(
                "Namespace {Namespace} does not exist on {ServerUrl}; registering it",
                LogSanitizer.Sanitize(@namespace), LogSanitizer.Sanitize(serverUrl));

            await client.WorkflowService.RegisterNamespaceAsync(new RegisterNamespaceRequest
            {
                Namespace = @namespace,
                WorkflowExecutionRetentionPeriod = Duration.FromTimeSpan(TimeSpan.FromDays(30))
            });
            // Temporal server may take a moment to be ready to accept search attribute registration after namespace creation.
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task DisposeAsync(TemporalClient? client)
    {
        if (client?.Connection is IAsyncDisposable asyncDisposableConnection)
        {
            await asyncDisposableConnection.DisposeAsync();
        }
        else if (client?.Connection is IDisposable disposableConnection)
        {
            disposableConnection.Dispose();
        }
    }
}
