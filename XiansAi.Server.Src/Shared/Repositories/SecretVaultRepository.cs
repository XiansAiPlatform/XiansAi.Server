using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface ISecretVaultRepository
{
    Task<SecretVault?> GetByIdAsync(string id);
    Task<SecretVault?> GetByKeyAsync(string key);
    Task<bool> ExistsByKeyAsync(string key);
    Task<bool> ExistsByKeyAndScopeAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName, string? excludeId = null);
    Task<SecretVault?> FindForAccessAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName);
    Task<List<SecretVault>> ListAsync(string? tenantId, string? agentId, string? activationName);
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
    /// Find a secret that matches key and scope. Strict match in both directions for tenantId, agentId, userId, and activationName:
    /// when request provides a scope value, document must have that exact value; when request omits (null/empty), document must have that scope null.
    /// </summary>
    public async Task<SecretVault?> FindForAccessAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName)
    {
        try
        {
            var filter = BuildKeyAndScopeFilter(key, tenantId, agentId, userId, activationName);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding secret for access key {Key}", key);
            return null;
        }
    }

    public async Task<bool> ExistsByKeyAndScopeAsync(string key, string? tenantId, string? agentId, string? userId, string? activationName, string? excludeId = null)
    {
        try
        {
            var filter = BuildKeyAndScopeFilter(key, tenantId, agentId, userId, activationName);
            if (!string.IsNullOrEmpty(excludeId))
            {
                var builder = Builders<SecretVault>.Filter;
                filter = builder.And(filter, builder.Ne(x => x.Id, excludeId));
            }

            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence for key {Key} and scope", key);
            return false;
        }
    }

    private static FilterDefinition<SecretVault> BuildKeyAndScopeFilter(string key, string? tenantId, string? agentId, string? userId, string? activationName)
    {
        var builder = Builders<SecretVault>.Filter;
        var filters = new List<FilterDefinition<SecretVault>>
        {
            builder.Eq(x => x.Key, key)
        };

        // Request scope must match document scope: if request omits scope, document must have that scope null.
        filters.Add(!string.IsNullOrWhiteSpace(tenantId)
            ? builder.Eq(x => x.TenantId, tenantId)
            : builder.Eq(x => x.TenantId, (string?)null));

        filters.Add(!string.IsNullOrWhiteSpace(agentId)
            ? builder.Eq(x => x.AgentId, agentId)
            : builder.Eq(x => x.AgentId, (string?)null));

        filters.Add(!string.IsNullOrWhiteSpace(userId)
            ? builder.Eq(x => x.UserId, userId)
            : builder.Eq(x => x.UserId, (string?)null));

        filters.Add(!string.IsNullOrWhiteSpace(activationName)
            ? builder.Eq(x => x.ActivationName, activationName)
            : builder.Eq(x => x.ActivationName, (string?)null));

        return builder.And(filters);
    }

    public async Task<List<SecretVault>> ListAsync(string? tenantId, string? agentId, string? activationName)
    {
        try
        {
            var builder = Builders<SecretVault>.Filter;
            var filter = builder.Empty;
            if (!string.IsNullOrWhiteSpace(tenantId))
                filter = builder.And(filter, builder.Eq(x => x.TenantId, tenantId));
            if (!string.IsNullOrWhiteSpace(agentId))
                filter = builder.And(filter, builder.Eq(x => x.AgentId, agentId));
            if (!string.IsNullOrWhiteSpace(activationName))
                filter = builder.And(filter, builder.Eq(x => x.ActivationName, activationName));
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
