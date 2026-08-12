using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

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
}

public class TenantTemporalConfigService : ITenantTemporalConfigService
{
    private readonly ITenantTemporalConfigRepository _repository;
    private readonly ILogger<TenantTemporalConfigService> _logger;

    public TenantTemporalConfigService(
        ITenantTemporalConfigRepository repository,
        ILogger<TenantTemporalConfigService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<ServiceResult<UpsertTenantTemporalConfigRequest?>> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<UpsertTenantTemporalConfigRequest?>.BadRequest("tenantId is required");

        try
        {
            // Repository only returns the active (non-deleted) row, already decrypted.
            var doc = await _repository.GetByTenantIdAsync(tenantId);
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
        if (string.IsNullOrWhiteSpace(serverUrl))
            return ServiceResult<bool>.BadRequest("serverUrl is required");
        if (string.IsNullOrWhiteSpace(@namespace))
            return ServiceResult<bool>.BadRequest("namespace is required");
        if (string.IsNullOrEmpty(certificate) != string.IsNullOrEmpty(privateKey))
            return ServiceResult<bool>.BadRequest("certificate and privateKey must be provided together");

        if (!TryDecodeBase64(certificate, out var certError))
            return ServiceResult<bool>.BadRequest($"certificate is not valid base64: {certError}");
        if (!TryDecodeBase64(privateKey, out var keyError))
            return ServiceResult<bool>.BadRequest($"privateKey is not valid base64: {keyError}");

        try
        {
            // to check tempolral connectivity
            // create namespace if it does not exist
            // also create the serch parameters for temporal.
            
            await _repository.UpsertAsync(tenantId, serverUrl, @namespace, certificate, privateKey, actor);
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
            // The row is kept, only flagged. See TenantTemporalConfigRepository.RevertAsync.
            var reverted = await _repository.RevertAsync(tenantId, actor);

            return reverted ? ServiceResult<bool>.Success(true) : ServiceResult<bool>.NotFound("No configuration found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Failed to revert Temporal configuration");
        }
    }

    private static bool TryDecodeBase64(string? value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrEmpty(value)) return true;
        try
        {
            Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
