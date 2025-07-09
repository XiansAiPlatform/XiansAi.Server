// Features/AgentApi/Repositories/ICertificateRepository.cs
using Features.AgentApi.Models;
using MongoDB.Driver;
using Shared.Data;
using Shared.Utils;

namespace Features.AgentApi.Repositories;

public interface ICertificateRepository
{
    Task<Certificate?> GetByThumbprintAsync(string thumbprint);
    Task<bool> IsRevokedAsync(string thumbprint);
    Task<bool> RevokeAsync(string thumbprint, string reason);
    Task CreateAsync(Certificate certificate);
    Task<IEnumerable<Certificate>> GetByUserAsync(string tenantId, string userId);
    Task UpdateAsync(Certificate certificate);
}

public class CertificateRepository : ICertificateRepository
{
    private readonly IMongoCollection<Certificate> _collection;
    private readonly ILogger<CertificateRepository> _logger;

    public CertificateRepository(
        IDatabaseService databaseService,
        ILogger<CertificateRepository> logger)
    {
        _logger = logger;
        var database = databaseService.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Certificate>("certificates");
    }

    public async Task<Certificate?> GetByThumbprintAsync(string thumbprint)
    {
        return await _collection
            .Find(cert => cert.Thumbprint == thumbprint)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsRevokedAsync(string thumbprint)
    {
        var certificate = await _collection
            .Find(cert => cert.Thumbprint == thumbprint)
            .Project(cert => new { cert.IsRevoked })
            .FirstOrDefaultAsync();
            
        return certificate?.IsRevoked ?? false;
    }

    public async Task<bool> RevokeAsync(string thumbprint, string reason)
    {
        // Sanitize and validate inputs ---ok---
        var sanitizedThumbprint = InputSanitizationUtils.SanitizeCertificateThumbprint(thumbprint);
        InputValidationUtils.ValidateCertificateThumbprint(sanitizedThumbprint, nameof(thumbprint));
        
        var sanitizedReason = InputSanitizationUtils.SanitizeRevocationReason(reason);
        InputValidationUtils.ValidateRevocationReason(sanitizedReason, nameof(reason));

        var update = Builders<Certificate>.Update
            .Set(x => x.IsRevoked, true)
            .Set(x => x.RevocationReason, sanitizedReason)
            .Set(x => x.RevokedAt, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(
            cert => cert.Thumbprint == sanitizedThumbprint,
            update);
        
        return result.ModifiedCount > 0;
    }

    public async Task CreateAsync(Certificate certificate)
    {
        try
        {
            await _collection.InsertOneAsync(certificate);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning("Attempted to create duplicate certificate with thumbprint: {Thumbprint}", 
                certificate.Thumbprint);
            throw new InvalidOperationException("Certificate already exists", ex);
        }
    }

    public async Task<IEnumerable<Certificate>> GetByUserAsync(string tenantId, string userId)
    {
        // Sanitize and validate inputs ---ok---
        var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
        InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));
        
        var sanitizedUserId = InputSanitizationUtils.SanitizeUserId(userId);
        InputValidationUtils.ValidateUserId(sanitizedUserId, nameof(userId));

        var filter = Builders<Certificate>.Filter.And(
            Builders<Certificate>.Filter.Eq(x => x.TenantId, sanitizedTenantId),
            Builders<Certificate>.Filter.Eq(x => x.IssuedTo, sanitizedUserId)
        );

        try
        {
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving certificates for user {UserId} in tenant {TenantId}", 
                sanitizedUserId, sanitizedTenantId);
            throw;
        }
    }

    public async Task UpdateAsync(Certificate certificate)
    {
        // Sanitize and validate certificate --- ok 
        var sanitizedCertificate = InputSanitizationUtils.SanitizeCertificate(certificate);
        InputValidationUtils.ValidateCertificate(sanitizedCertificate);

        if (sanitizedCertificate == null)
        {
            throw new ArgumentException("Certificate cannot be null after sanitization");
        }

        try
        {
            sanitizedCertificate.UpdatedAt = DateTime.UtcNow;
            
            var filter = Builders<Certificate>.Filter.And(
                Builders<Certificate>.Filter.Eq(x => x.Thumbprint, sanitizedCertificate.Thumbprint),
                Builders<Certificate>.Filter.Eq(x => x.TenantId, sanitizedCertificate.TenantId)
            );

            var result = await _collection.ReplaceOneAsync(filter, sanitizedCertificate);

            if (result.ModifiedCount == 0 && result.MatchedCount == 0)
            {
                _logger.LogWarning("Certificate not found for update. Thumbprint: {Thumbprint}, TenantId: {TenantId}",
                    sanitizedCertificate.Thumbprint, sanitizedCertificate.TenantId);
                throw new InvalidOperationException($"Certificate with thumbprint {sanitizedCertificate.Thumbprint} not found");
            }
        }
        catch (MongoWriteException ex)
        {
            _logger.LogError(ex, "Error updating certificate {Thumbprint}", sanitizedCertificate.Thumbprint);
            throw new InvalidOperationException("Failed to update certificate", ex);
        }
    }
}