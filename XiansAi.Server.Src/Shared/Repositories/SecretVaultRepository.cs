using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface ISecretVaultRepository
{
    Task<SecretVault?> GetByIdAsync(string id);
    Task<SecretVault?> GetByKeyAsync(string key);
    Task<bool> ExistsByKeyAsync(string key);
    Task<SecretVault?> FindForAccessAsync(string key, string? tenantId, string? agentId, string? userId);
    Task<List<SecretVault>> ListAsync(string? tenantId, string? agentId);
    Task CreateAsync(SecretVault entity);
    Task<bool> UpdateAsync(SecretVault entity);
    Task<bool> DeleteAsync(string id);
}

public class SecretVaultRepository : ISecretVaultRepository
{
    private readonly IMongoCollection<SecretVault> _collection;
    private readonly ILogger<SecretVaultRepository> _logger;

    public SecretVaultRepository(IDatabaseService databaseService, ILogger<SecretVaultRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<SecretVault>("secret_vault");
        _logger = logger;
    }

    public async Task<SecretVault?> GetByIdAsync(string id)
    {
        try
        {
            return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret vault by id {Id}", id);
            return null;
        }
    }

    public async Task<SecretVault?> GetByKeyAsync(string key)
    {
        try
        {
            return await _collection.Find(x => x.Key == key).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret vault by key {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsByKeyAsync(string key)
    {
        try
        {
            var count = await _collection.CountDocumentsAsync(Builders<SecretVault>.Filter.Eq(x => x.Key, key));
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence for key {Key}", key);
            return false;
        }
    }

    /// <summary>
    /// Find a secret that matches key and scope. Strict match in both directions:
    /// - When request provides tenantId/agentId/userId, document must have that exact value.
    /// - When request does NOT provide a scope (null/empty), document must have that scope null (cross-tenant/agent/user).
    /// So a document with tenant_id=99xio is only returned when request sends tenantId=99xio; fetch with no tenantId returns nothing for it.
    /// </summary>
    public async Task<SecretVault?> FindForAccessAsync(string key, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            var keyFilter = Builders<SecretVault>.Filter.Eq(x => x.Key, key);
            var filters = new List<MongoDB.Driver.FilterDefinition<SecretVault>> { keyFilter };

            // Request scope must match document scope: if request omits scope, document must have that scope null.
            if (!string.IsNullOrWhiteSpace(tenantId))
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.TenantId, tenantId));
            else
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.TenantId, (string?)null));

            if (!string.IsNullOrWhiteSpace(agentId))
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.AgentId, agentId));
            else
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.AgentId, (string?)null));

            if (!string.IsNullOrWhiteSpace(userId))
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.UserId, userId));
            else
                filters.Add(Builders<SecretVault>.Filter.Eq(x => x.UserId, (string?)null));

            var filter = Builders<SecretVault>.Filter.And(filters);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding secret for access key {Key}", key);
            return null;
        }
    }

    public async Task<List<SecretVault>> ListAsync(string? tenantId, string? agentId)
    {
        try
        {
            var builder = Builders<SecretVault>.Filter;
            var filter = builder.Empty;
            if (!string.IsNullOrWhiteSpace(tenantId))
                filter = builder.And(filter, builder.Eq(x => x.TenantId, tenantId));
            if (!string.IsNullOrWhiteSpace(agentId))
                filter = builder.And(filter, builder.Eq(x => x.AgentId, agentId));
            return await _collection.Find(filter).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secret vault");
            return new List<SecretVault>();
        }
    }

    public async Task CreateAsync(SecretVault entity)
    {
        try
        {
            await _collection.InsertOneAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret vault key {Key}", entity.Key);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(SecretVault entity)
    {
        try
        {
            var result = await _collection.ReplaceOneAsync(x => x.Id == entity.Id, entity);
            return result.ModifiedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating secret vault id {Id}", entity.Id);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        try
        {
            var result = await _collection.DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret vault id {Id}", id);
            return false;
        }
    }
}
