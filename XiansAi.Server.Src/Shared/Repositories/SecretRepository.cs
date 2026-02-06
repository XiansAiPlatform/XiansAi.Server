using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface ISecretRepository
{
    Task<Secret?> GetByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId);
    Task<(List<Secret> items, int totalCount)> ListByScopesAsync(string? tenantId, string? agentId, string? userId, string? secretIdPattern, int page, int pageSize);
    Task<bool> ExistsByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId);
    Task CreateAsync(Secret secret);
    Task UpdateAsync(Secret secret);
    Task<bool> DeleteByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId);
}

public class SecretRepository : ISecretRepository
{
    private readonly IMongoCollection<Secret> _collection;
    private readonly ILogger<SecretRepository> _logger;

    public SecretRepository(IDatabaseService databaseService, ILogger<SecretRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<Secret>("secrets");
        _logger = logger;

        // Create indexes for efficient querying
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        try
        {
            // Unique compound index for scope lookups
            var scopeIndexKeys = Builders<Secret>.IndexKeys
                .Ascending(s => s.SecretId)
                .Ascending(s => s.TenantId)
                .Ascending(s => s.AgentId)
                .Ascending(s => s.UserId);
            var scopeIndexOptions = new CreateIndexOptions { Unique = true };
            _collection.Indexes.CreateOne(new CreateIndexModel<Secret>(scopeIndexKeys, scopeIndexOptions));
            _logger.LogDebug("Created unique index on (secretId, tenantId, agentId, userId)");

            // TTL index: auto-delete documents when expire_at has passed
            // Note: MongoDB automatically ignores documents where expire_at is null/missing
            var ttlIndexKeys = Builders<Secret>.IndexKeys.Ascending(s => s.ExpireAt);
            var ttlIndexOptions = new CreateIndexOptions
            {
                ExpireAfter = TimeSpan.Zero // Delete when expire_at < now (UTC)
            };
            _collection.Indexes.CreateOne(new CreateIndexModel<Secret>(ttlIndexKeys, ttlIndexOptions));
            _logger.LogDebug("Created TTL index on expire_at (documents with null/missing expire_at are not affected)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for secrets collection (may already exist)");
        }
    }

    public async Task<Secret?> GetByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            var filter = BuildFilter(secretId, tenantId, agentId, userId);
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret with secretId={SecretId}, tenantId={TenantId}, agentId={AgentId}, userId={UserId}",
                secretId, tenantId, agentId, userId);
            return null;
        }
    }

    public async Task<(List<Secret> items, int totalCount)> ListByScopesAsync(string? tenantId, string? agentId, string? userId, string? secretIdPattern, int page, int pageSize)
    {
        try
        {
            var filter = BuildListFilter(tenantId, agentId, userId, secretIdPattern);
            var totalCount = (int)await _collection.CountDocumentsAsync(filter);

            var skip = (page - 1) * pageSize;
            var items = await _collection
                .Find(filter)
                .SortByDescending(s => s.CreatedAt)
                .Skip(skip)
                .Limit(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secrets with tenantId={TenantId}, agentId={AgentId}, userId={UserId}",
                tenantId, agentId, userId);
            return (new List<Secret>(), 0);
        }
    }

    public async Task<bool> ExistsByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            var filter = BuildFilter(secretId, tenantId, agentId, userId);
            var count = await _collection.CountDocumentsAsync(filter);
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of secret with secretId={SecretId}, tenantId={TenantId}, agentId={AgentId}, userId={UserId}",
                secretId, tenantId, agentId, userId);
            return false;
        }
    }

    public async Task CreateAsync(Secret secret)
    {
        try
        {
            await _collection.InsertOneAsync(secret);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(ex, "Duplicate key error creating secret with secretId={SecretId}, tenantId={TenantId}, agentId={AgentId}, userId={UserId}",
                secret.SecretId, secret.TenantId, secret.AgentId, secret.UserId);
            throw new InvalidOperationException("Secret already exists in this scope");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating secret with secretId={SecretId}", secret.SecretId);
            throw;
        }
    }

    public async Task UpdateAsync(Secret secret)
    {
        try
        {
            var filter = Builders<Secret>.Filter.Eq(s => s.Id, secret.Id);
            await _collection.ReplaceOneAsync(filter, secret);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating secret with id={Id}", secret.Id);
            throw;
        }
    }

    public async Task<bool> DeleteByScopesAsync(string secretId, string? tenantId, string? agentId, string? userId)
    {
        try
        {
            var filter = BuildFilter(secretId, tenantId, agentId, userId);
            var result = await _collection.DeleteOneAsync(filter);
            return result.DeletedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret with secretId={SecretId}, tenantId={TenantId}, agentId={AgentId}, userId={UserId}",
                secretId, tenantId, agentId, userId);
            return false;
        }
    }

    private FilterDefinition<Secret> BuildFilter(string secretId, string? tenantId, string? agentId, string? userId)
    {
        var filters = new List<FilterDefinition<Secret>>
        {
            Builders<Secret>.Filter.Eq(s => s.SecretId, secretId)
        };

        if (tenantId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.TenantId, tenantId));
        }
        else
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.TenantId, (string?)null));
        }

        if (agentId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.AgentId, agentId));
        }
        else
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.AgentId, (string?)null));
        }

        if (userId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.UserId, userId));
        }
        else
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.UserId, (string?)null));
        }

        return Builders<Secret>.Filter.And(filters);
    }

    private FilterDefinition<Secret> BuildListFilter(string? tenantId, string? agentId, string? userId, string? secretIdPattern)
    {
        var filters = new List<FilterDefinition<Secret>>();

        if (tenantId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.TenantId, tenantId));
        }

        if (agentId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.AgentId, agentId));
        }

        if (userId != null)
        {
            filters.Add(Builders<Secret>.Filter.Eq(s => s.UserId, userId));
        }

        if (!string.IsNullOrWhiteSpace(secretIdPattern))
        {
            // Support regex pattern matching for secretId
            var regex = new MongoDB.Bson.BsonRegularExpression(secretIdPattern, "i");
            filters.Add(Builders<Secret>.Filter.Regex(s => s.SecretId, regex));
        }

        return filters.Count > 0
            ? Builders<Secret>.Filter.And(filters)
            : Builders<Secret>.Filter.Empty;
    }
}

