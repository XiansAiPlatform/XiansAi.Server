using System.Security.Cryptography;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

public interface ITenantTemporalConfigService
{
    Task<TenantTemporalConfig> SaveAsync(string tenantId, string host, string @namespace, string? certificate, string? certificateKey, string createdBy);
    Task<TenantTemporalConfig?> GetForTenantAsync(string tenantId);
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
            {
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            }
            _uniqueSecret = baseSecret;
        }
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

        var encryptedCertificate = certificate == null ? null : _encryption.Encrypt(certificate, _uniqueSecret);
        var encryptedCertificateKey = certificateKey == null ? null : _encryption.Encrypt(certificateKey, _uniqueSecret);

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
                Certificate = encryptedCertificate,
                CertificateKey = encryptedCertificateKey,
                CreatedAt = now,
                CreatedBy = createdBy
            };
        }
        else
        {
            tenantTemporal = existing;
            tenantTemporal.Host = host;
            tenantTemporal.Namespace = @namespace;
            tenantTemporal.Certificate = encryptedCertificate;
            tenantTemporal.CertificateKey = encryptedCertificateKey;
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

        var config = await _repository.GetByTenantIdAsync(tenantId);
        if (config == null)
            return null;

        try
        {
            config.Certificate = config.Certificate == null ? null : _encryption.Decrypt(config.Certificate, _uniqueSecret);
            config.CertificateKey = config.CertificateKey == null ? null : _encryption.Decrypt(config.CertificateKey, _uniqueSecret);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            _logger.LogError(ex, "Failed to decrypt Temporal mTLS credentials for tenant {TenantId}. The encryption keys may have changed.", LogSanitizer.Sanitize(tenantId));
            throw;
        }

        return config;
    }
}
