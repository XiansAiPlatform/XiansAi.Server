using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITenantTemporalConfigRepository
{
    Task<TenantTemporalConfig?> GetAsync(string tenantId);
    Task<TenantTemporalConfig?> GetAsync(string tenantId, string serverUrl);

    Task UpsertAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey, string actor);

    Task<bool> RevertAsync(string tenantId, string actor);
}

public class TenantTemporalConfigRepository : ITenantTemporalConfigRepository
{
    private readonly IMongoCollection<TenantTemporalConfig> _collection;
    private readonly ILogger<TenantTemporalConfigRepository> _logger;
    private readonly ISecureEncryptionService _encryption;
    private readonly string _uniqueSecret;

    public TenantTemporalConfigRepository(
        IDatabaseService databaseService,
        ILogger<TenantTemporalConfigRepository> logger,
        ISecureEncryptionService encryption,
        IConfiguration configuration)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TenantTemporalConfig>("tenant_temporal_config");
        _logger = logger;
        _encryption = encryption;
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

    public async Task<TenantTemporalConfig?> GetAsync(string tenantId)
    {
        try
        {
            var doc = await _collection.Find(x => x.TenantId == tenantId && !x.IsReverted).FirstOrDefaultAsync();
            if (doc != null)
            {
                doc.Certificate = doc.Certificate == null ? null : _encryption.Decrypt(doc.Certificate, _uniqueSecret);
                doc.PrivateKey = doc.PrivateKey == null ? null : _encryption.Decrypt(doc.PrivateKey, _uniqueSecret);
            }
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }

    public async Task<TenantTemporalConfig?> GetAsync(string tenantId, string serverUrl)
    {
        try
        {
            var doc = await _collection.Find(x => x.TenantId == tenantId && x.ServerUrl == serverUrl && !x.IsReverted).FirstOrDefaultAsync();
            if (doc != null)
            {
                doc.Certificate = doc.Certificate == null ? null : _encryption.Decrypt(doc.Certificate, _uniqueSecret);
                doc.PrivateKey = doc.PrivateKey == null ? null : _encryption.Decrypt(doc.PrivateKey, _uniqueSecret);
            }
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }


    public async Task UpsertAsync(string tenantId, string serverUrl, string @namespace, string? certificate, string? privateKey, string actor)
    {
        try
        {
            var encryptedCertificate = certificate == null ? null : _encryption.Encrypt(certificate, _uniqueSecret);
            var encryptedPrivateKey = privateKey == null ? null : _encryption.Encrypt(privateKey, _uniqueSecret);

            var filter = Builders<TenantTemporalConfig>.Filter.And(
                        Builders<TenantTemporalConfig>.Filter.Eq(t => t.TenantId, tenantId),
                        Builders<TenantTemporalConfig>.Filter.Eq(t => t.IsReverted, false)
                    );
            var update = Builders<TenantTemporalConfig>.Update
                .Set(x => x.ServerUrl, serverUrl)
                .Set(x => x.Namespace, @namespace)
                .Set(x => x.Certificate, encryptedCertificate)
                .Set(x => x.PrivateKey, encryptedPrivateKey)
                .Set(x => x.UpdatedBy, actor)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow)
                .SetOnInsert(x => x.CreatedBy, actor);
            var options = new UpdateOptions { IsUpsert = true };
            var result = await _collection.UpdateOneAsync(filter, update, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }

    public async Task<bool> RevertAsync(string tenantId, string actorUserId)
    {
        try
        {
            var update = Builders<TenantTemporalConfig>.Update
                .Set(x => x.IsReverted, true)
                .Set(x => x.RevertedAt, DateTime.UtcNow)
                .Set(x => x.RevertedBy, actorUserId);

            var result = await _collection.UpdateOneAsync(x => x.TenantId == tenantId && !x.IsReverted, update);
            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return false;
        }
    }

    private TenantTemporalConfig Decrypt(TenantTemporalConfig doc)
    {
        doc.Certificate = doc.Certificate == null ? null : _encryption.Decrypt(doc.Certificate, _uniqueSecret);
        doc.PrivateKey = doc.PrivateKey == null ? null : _encryption.Decrypt(doc.PrivateKey, _uniqueSecret);
        return doc;
    }
}
