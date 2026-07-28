using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services;

public interface ITenantTemporalConfigService
{
    Task<TenantTemporalConfig> SaveAsync(string tenantId, string host, string @namespace, string? certificate, string? certificateKey, string createdBy);
    Task<TenantTemporalConfig?> GetForTenantAsync(string tenantId);
}

public class TenantTemporalConfigService : ITenantTemporalConfigService
{
    private readonly ITenantTemporalConfigRepository _repository;

    public TenantTemporalConfigService(ITenantTemporalConfigRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<TenantTemporalConfig> SaveAsync(string tenantId, string host, string @namespace, string? certificate, string? certificateKey, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("tenantId is required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("host is required", nameof(host));
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("namespace is required", nameof(@namespace));
        if (string.IsNullOrWhiteSpace(certificate) != string.IsNullOrWhiteSpace(certificateKey))
            throw new ArgumentException("certificate and certificateKey must be provided together");

        var existing = await _repository.GetByTenantIdAsync(tenantId);
        var now = DateTime.UtcNow;

        TenantTemporalConfig tenantTemporal;
        if (existing == null)
        {
            tenantTemporal = new TenantTemporalConfig
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = tenantId,
                Host = host,
                Namespace = @namespace,
                Certificate = certificate,
                CertificateKey = certificateKey,
                CreatedAt = now,
                CreatedBy = createdBy
            };
        }
        else
        {
            tenantTemporal = existing;
            tenantTemporal.Host = host;
            tenantTemporal.Namespace = @namespace;
            tenantTemporal.Certificate = certificate;
            tenantTemporal.CertificateKey = certificateKey;
            tenantTemporal.UpdatedAt = now;
            tenantTemporal.UpdatedBy = createdBy;
        }

        await _repository.UpsertAsync(tenantTemporal);
        return tenantTemporal;
    }

    public async Task<TenantTemporalConfig?> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        return await _repository.GetByTenantIdAsync(tenantId);
    }
}
