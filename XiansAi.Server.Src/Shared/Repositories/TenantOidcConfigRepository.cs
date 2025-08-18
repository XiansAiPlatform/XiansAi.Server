using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface ITenantOidcConfigRepository
{
    Task<TenantOidcConfig?> GetByTenantIdAsync(string tenantId);
    Task UpsertAsync(TenantOidcConfig config);
    Task<bool> DeleteAsync(string tenantId);
    Task<List<TenantOidcConfig>> GetAllAsync();
}

public class TenantOidcConfigRepository : ITenantOidcConfigRepository
{
    private readonly IMongoCollection<TenantOidcConfig> _collection;
    private readonly ILogger<TenantOidcConfigRepository> _logger;

    public TenantOidcConfigRepository(IDatabaseService databaseService, ILogger<TenantOidcConfigRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TenantOidcConfig>("tenant_oidc_config");
        _logger = logger;
    }

    public async Task<TenantOidcConfig?> GetByTenantIdAsync(string tenantId)
    {
        try
        {
            return await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving OIDC config for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task UpsertAsync(TenantOidcConfig config)
    {
        try
        {
            var filter = Builders<TenantOidcConfig>.Filter.Eq(x => x.TenantId, config.TenantId);
            await _collection.ReplaceOneAsync(filter, config, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting OIDC config for tenant {TenantId}", config.TenantId);
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
            _logger.LogError(ex, "Error deleting OIDC config for tenant {TenantId}", tenantId);
            return false;
        }
    }

    public async Task<List<TenantOidcConfig>> GetAllAsync()
    {
        try
        {
            return await _collection
                .Find(Builders<TenantOidcConfig>.Filter.Empty)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all OIDC configs");
            return new List<TenantOidcConfig>();
        }
    }
}

