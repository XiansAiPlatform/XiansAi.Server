using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface IGenericOidcConfigRepository
{
    Task<GenericOidcConfig?> GetByTenantIdAsync(string tenantId);
    Task UpsertAsync(GenericOidcConfig config);
    Task<bool> DeleteAsync(string tenantId);
    Task<List<GenericOidcConfig>> GetAllAsync();
}

public class GenericOidcConfigRepository : IGenericOidcConfigRepository
{
    private readonly IMongoCollection<GenericOidcConfig> _collection;
    private readonly ILogger<GenericOidcConfigRepository> _logger;

    public GenericOidcConfigRepository(IDatabaseService databaseService, ILogger<GenericOidcConfigRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<GenericOidcConfig>("generic_oidc_config");
        _logger = logger;
    }

    public async Task<GenericOidcConfig?> GetByTenantIdAsync(string tenantId)
    {
        try
        {
            return await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Generic OIDC config for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task UpsertAsync(GenericOidcConfig config)
    {
        try
        {
            var filter = Builders<GenericOidcConfig>.Filter.Eq(x => x.TenantId, config.TenantId);
            await _collection.ReplaceOneAsync(filter, config, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting Generic OIDC config for tenant {TenantId}", config.TenantId);
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
            _logger.LogError(ex, "Error deleting Generic OIDC config for tenant {TenantId}", tenantId);
            return false;
        }
    }

    public async Task<List<GenericOidcConfig>> GetAllAsync()
    {
        try
        {
            return await _collection
                .Find(Builders<GenericOidcConfig>.Filter.Empty)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing all Generic OIDC configs");
            return new List<GenericOidcConfig>();
        }
    }
}

