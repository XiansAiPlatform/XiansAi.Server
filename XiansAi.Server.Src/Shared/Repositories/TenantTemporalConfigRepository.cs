using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITenantTemporalConfigRepository
{
    Task<TenantTemporalConfig?> GetByTenantIdAsync(string tenantId);
    Task UpsertAsync(TenantTemporalConfig config);
}

public class TenantTemporalConfigRepository : ITenantTemporalConfigRepository
{
    private readonly IMongoCollection<TenantTemporalConfig> _collection;
    private readonly ILogger<TenantTemporalConfigRepository> _logger;

    public TenantTemporalConfigRepository(IMongoDbClientService mongoDbClientService, ILogger<TenantTemporalConfigRepository> logger)
    {
        _collection = mongoDbClientService.GetCollection<TenantTemporalConfig>("tenant_temporal_config");
        _logger = logger;
    }

    public async Task<TenantTemporalConfig?> GetByTenantIdAsync(string tenantId)
    {
        try
        {
            return await _collection.Find(x => x.TenantId == tenantId).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return null;
        }
    }

    public async Task UpsertAsync(TenantTemporalConfig config)
    {
        try
        {
            var filter = Builders<TenantTemporalConfig>.Filter.Eq(x => x.TenantId, config.TenantId);
            await _collection.ReplaceOneAsync(filter, config, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting Temporal config for tenant {TenantId}", LogSanitizer.Sanitize(config.TenantId));
            throw;
        }
    }
}
