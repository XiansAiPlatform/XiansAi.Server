// Features/AgentApi/Repositories/ICertificateRepository.cs
using Features.AgentApi.Models;
using MongoDB.Driver;
using Shared.Data;
using Shared.Utils;
using XiansAi.Server.Shared.Data;

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
    private readonly Lazy<Task> _indexCreationTask;
    private volatile bool _indexesCreated = false;

    public CertificateRepository(
        IDatabaseService databaseService,
        ILogger<CertificateRepository> logger)
    {
        _logger = logger;
        var database = databaseService.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Certificate>("certificates");
        
        // Initialize indexes asynchronously without blocking constructor
        _indexCreationTask = new Lazy<Task>(() => InitializeIndexesAsync());
        
        // Start index creation in background (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexCreationTask.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background index creation failed during CertificateRepository initialization");
            }
        });
    }

    /// <summary>
    /// Initializes indexes asynchronously with resilient error handling.
    /// This method won't throw exceptions and allows the application to continue running even if MongoDB is temporarily unavailable.
    /// </summary>
    private async Task InitializeIndexesAsync()
    {
        if (_indexesCreated) return;

        var success = await MongoRetryHelper.ExecuteWithGracefulRetryAsync(async () =>
        {
            // Create indexes for performance
            var indexModels = new[]
            {
                new CreateIndexModel<Certificate>(
                    Builders<Certificate>.IndexKeys.Ascending(x => x.Thumbprint),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<Certificate>(
                    Builders<Certificate>.IndexKeys.Ascending(x => x.TenantId)),
                new CreateIndexModel<Certificate>(
                    Builders<Certificate>.IndexKeys.Ascending(x => x.ExpiresAt))
            };
            
            await _collection.Indexes.CreateManyAsync(indexModels);

        }, _logger, maxRetries: 2, baseDelayMs: 2000, operationName: "CreateCertificateIndexes");

        if (success)
        {
            _indexesCreated = true;
            _logger.LogInformation("Successfully created indexes for Certificate collection");
        }
        else
        {
            _logger.LogWarning("Failed to create indexes for Certificate collection, but repository will continue to function. Indexes will be retried on next operation.");
        }
    }

    /// <summary>
    /// Ensures indexes are created before performing operations (lazy initialization).
    /// This method is called by repository methods that benefit from having indexes.
    /// </summary>
    private async Task EnsureIndexesAsync()
    {
        if (!_indexesCreated)
        {
            try
            {
                await _indexCreationTask.Value;
            }
            catch (Exception ex)
            {
                // Log but don't throw - the operation can continue without indexes
                _logger.LogDebug(ex, "Index creation not yet complete, continuing with operation");
            }
        }
    }

    public async Task<Certificate?> GetByThumbprintAsync(string thumbprint)
    {
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
        return await _collection
            .Find(cert => cert.Thumbprint == thumbprint)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsRevokedAsync(string thumbprint)
    {
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
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
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
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