// Features/AgentApi/Repositories/ICertificateRepository.cs
using Features.AgentApi.Models;
using Features.AgentApi.Auth;
using MongoDB.Driver;
using Shared.Data;

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
    private readonly ICertificateValidationCache _validationCache;

    public CertificateRepository(
        IDatabaseService databaseService,
        ILogger<CertificateRepository> logger,
        ICertificateValidationCache validationCache)
    {
        _logger = logger;
        _validationCache = validationCache;
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
        var update = Builders<Certificate>.Update
            .Set(x => x.IsRevoked, true)
            .Set(x => x.RevocationReason, reason)
            .Set(x => x.RevokedAt, DateTime.UtcNow)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(
            cert => cert.Thumbprint == thumbprint,
            update);
        
        if (result.ModifiedCount > 0)
        {
            // Invalidate cache entry for revoked certificate
            _validationCache.RemoveValidation(thumbprint);
            _logger.LogInformation("Revoked certificate and invalidated cache for thumbprint: {Thumbprint}", thumbprint);
        }
        
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
        var filter = Builders<Certificate>.Filter.And(
            Builders<Certificate>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<Certificate>.Filter.Eq(x => x.IssuedTo, userId)
        );

        try
        {
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving certificates for user {UserId} in tenant {TenantId}", 
                userId, tenantId);
            throw;
        }
    }

    public async Task UpdateAsync(Certificate certificate)
    {
        try
        {
            certificate.UpdatedAt = DateTime.UtcNow;
            
            var filter = Builders<Certificate>.Filter.And(
                Builders<Certificate>.Filter.Eq(x => x.Thumbprint, certificate.Thumbprint),
                Builders<Certificate>.Filter.Eq(x => x.TenantId, certificate.TenantId)
            );

            var result = await _collection.ReplaceOneAsync(filter, certificate);

            if (result.ModifiedCount == 0 && result.MatchedCount == 0)
            {
                _logger.LogWarning("Certificate not found for update. Thumbprint: {Thumbprint}, TenantId: {TenantId}",
                    certificate.Thumbprint, certificate.TenantId);
                throw new InvalidOperationException($"Certificate with thumbprint {certificate.Thumbprint} not found");
            }
        }
        catch (MongoWriteException ex)
        {
            _logger.LogError(ex, "Error updating certificate {Thumbprint}", certificate.Thumbprint);
            throw new InvalidOperationException("Failed to update certificate", ex);
        }
    }
}