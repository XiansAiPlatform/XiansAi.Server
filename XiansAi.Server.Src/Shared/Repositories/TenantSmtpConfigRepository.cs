using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Services;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITenantSmtpConfigRepository
{
    /// <summary>Returns the tenant's SMTP config with the password decrypted, or null.</summary>
    Task<TenantSmtpConfig?> GetAsync(string tenantId);

    /// <summary>Creates or replaces the tenant's SMTP config. The password is encrypted before saving.</summary>
    Task UpsertAsync(string tenantId, SmtpSettings settings, string actor);

    Task<bool> DeleteAsync(string tenantId);
}

public class TenantSmtpConfigRepository : ITenantSmtpConfigRepository
{
    private readonly IMongoCollection<TenantSmtpConfig> _collection;
    private readonly ILogger<TenantSmtpConfigRepository> _logger;
    private readonly ISecureEncryptionService _encryption;
    private readonly string _uniqueSecret;

    public TenantSmtpConfigRepository(
        IDatabaseService databaseService,
        ILogger<TenantSmtpConfigRepository> logger,
        ISecureEncryptionService encryption,
        IConfiguration configuration)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TenantSmtpConfig>("tenant_smtp_config");
        _logger = logger;
        _encryption = encryption;
        _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:TenantSmtpSecretKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_uniqueSecret))
        {
            _logger.LogWarning("EncryptionKeys:UniqueSecrets:TenantSmtpSecretKey is not configured. Using the base secret value.");
            var baseSecret = configuration["EncryptionKeys:BaseSecret"];
            if (string.IsNullOrWhiteSpace(baseSecret))
            {
                throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
            }
            _uniqueSecret = baseSecret;
        }
    }

    public async Task<TenantSmtpConfig?> GetAsync(string tenantId)
    {
        try
        {
            var doc = await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync();
            if (doc?.Password != null)
            {
                doc.Password = _encryption.Decrypt(doc.Password, _uniqueSecret);
            }
            return doc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }

    public async Task UpsertAsync(string tenantId, SmtpSettings settings, string actor)
    {
        try
        {
            var encryptedPassword = string.IsNullOrEmpty(settings.Password)
                ? null
                : _encryption.Encrypt(settings.Password, _uniqueSecret);

            var update = Builders<TenantSmtpConfig>.Update
                .Set(x => x.Host, settings.Host)
                .Set(x => x.Port, settings.Port)
                .Set(x => x.Security, settings.Security)
                .Set(x => x.Username, settings.Username)
                .Set(x => x.Password, encryptedPassword)
                .Set(x => x.SenderEmail, settings.SenderEmail)
                .Set(x => x.SenderName, settings.SenderName)
                .Set(x => x.UpdatedBy, actor)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow)
                .SetOnInsert(x => x.CreatedBy, actor);

            await _collection.UpdateOneAsync(x => x.TenantId == tenantId, update, new UpdateOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string tenantId)
    {
        try
        {
            var result = await _collection.DeleteOneAsync(x => x.TenantId == tenantId);
            return result.DeletedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            throw;
        }
    }
}
