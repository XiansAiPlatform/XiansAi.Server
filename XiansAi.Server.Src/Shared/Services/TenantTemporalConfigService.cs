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
    private readonly ITemporalClientService _temporalClientService;

    public TenantTemporalConfigService(
        IServiceScopeFactory serviceFactory,
        ILogger<TenantTemporalConfigService> logger)
    {
        _logger = logger;

        using var scope = serviceFactory.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<ITenantTemporalConfigRepository>();
        _temporalClientService = scope.ServiceProvider.GetRequiredService<ITemporalClientService>();
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
            await _temporalClientService.RemoveClient(tenantId);
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
            await _temporalClientService.RemoveClient(tenantId);
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
            await EnsureSearchAttributesExistAsync(client, @namespace);
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
    private async Task EnsureSearchAttributesExistAsync(TemporalClient client, string @namespace)
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
