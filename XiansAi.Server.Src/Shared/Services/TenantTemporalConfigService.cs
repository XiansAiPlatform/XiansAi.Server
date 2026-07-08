using System.Text.Json;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Temporal;

namespace Shared.Services;

public interface ITenantTemporalConfigService
{
    Task<TenantTemporalConfig?> CreateAsync(string tenantId, string rootCertificate, string rootCertificateKey, string leafCertificate, string leafCertificateKey, string createdBy);
}

public class TenantTemporalConfigService : ITenantTemporalConfigService
{
    private readonly ITenantTemporalConfigRepository _repository;
    private readonly ISecureEncryptionService _encryption;
    private readonly ILogger<TenantTemporalConfigService> _logger;
    private readonly string _uniqueSecret;

    public TenantTemporalConfigService(
        ITenantTemporalConfigRepository repository,
        ISecureEncryptionService encryption,
        ILogger<TenantTemporalConfigService> logger,
        IConfiguration configuration)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:TenantTemporalSecretKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:TenantTemporalSecretKey is not configured. Using the base secret value.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<TenantTemporalConfig?> CreateAsync(string tenantId, string rootCertificate, string rootCertificateKey, string leafCertificate, string leafCertificateKey, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return null;

        TenantTemporalConfig tenantTemporal;

        var doc = await _repository.GetByTenantIdAsync(tenantId);
        if (doc == null)
        {
            tenantTemporal = new TenantTemporalConfig
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = tenantId,
                RootCertificate = rootCertificate,
                RootCertificateKey = rootCertificateKey,
                LeafCertificate = leafCertificate,
                LeafCertificateKey = leafCertificateKey,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            };
        }
        else
        {
            tenantTemporal = doc;

            tenantTemporal.RootCertificate = rootCertificate;
            tenantTemporal.RootCertificateKey = rootCertificateKey;
            tenantTemporal.LeafCertificate = leafCertificate;
            tenantTemporal.LeafCertificateKey = leafCertificateKey;
            tenantTemporal.UpdatedAt = DateTime.UtcNow;
            tenantTemporal.UpdatedBy = createdBy;
        }

        await _repository.UpsertAsync(tenantTemporal);
        return tenantTemporal;
    }
}
