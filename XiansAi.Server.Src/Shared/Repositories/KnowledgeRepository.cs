using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;
using Shared.Utils;

namespace Shared.Repositories;

public interface IKnowledgeRepository
{
    Task<T?> GetLatestByNameAsync<T>(string name, string agent, string? tenantId) where T : IKnowledge;
    Task<T?> GetLatestSystemByNameAsync<T>(string name, string agent) where T : IKnowledge;
    Task<T> GetByIdAsync<T>(string id) where T : IKnowledge;
    Task<T> GetByVersionAsync<T>(string version) where T : IKnowledge;
    Task<List<T>> GetByNameAsync<T>(string name, string? agent, string tenantId) where T : IKnowledge;
    Task<List<T>> GetAllAsync<T>(string tenantId) where T : IKnowledge;
    Task CreateAsync<T>(T knowledge) where T : IKnowledge;
    Task<bool> DeleteAsync<T>(string id) where T : IKnowledge;
    Task<bool> UpdateAsync<T>(string id, T knowledge) where T : IKnowledge;
    Task<T?> GetByNameVersionAsync<T>(string name, string version, string agent, string tenantId) where T : IKnowledge;
    Task<List<T>> SearchAsync<T>(string searchTerm, string tenantId) where T : IKnowledge;
    Task<List<T>> GetUniqueLatestAsync<T>(string? tenantId, List<string> agentNames) where T : IKnowledge;
    Task<List<T>> GetUniqueLatestSystemScopedAsync<T>(List<string> agentNames) where T : IKnowledge;
    Task<List<T>> GetUniqueLatestTenantScopedAsync<T>(string? tenantId, List<string> agentNames) where T : IKnowledge;
    Task<bool> DeleteAllVersionsAsync<T>(string name, string? agent, string? tenantId) where T : IKnowledge;
    Task<List<T>> GetSystemScopedByAgentAsync<T>(string agentName) where T : IKnowledge;
}

public class KnowledgeRepository : IKnowledgeRepository
{
    private readonly IMongoCollection<BsonDocument> _knowledge;
    private readonly ILogger<KnowledgeRepository> _logger;

    public KnowledgeRepository(
        IDatabaseService databaseService,
        ILogger<KnowledgeRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _knowledge = database.GetCollection<BsonDocument>("knowledge");
        _logger = logger;
    }

    public async Task<T?> GetLatestByNameAsync<T>(string name, string agent, string? tenantId) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            
            var nameFilter = Builders<T>.Filter.Eq(x => x.Name, name);
            
            // Create an agent filter that matches either the specified agent or null agent
            var agentFilter = !string.IsNullOrEmpty(agent) 
                ? Builders<T>.Filter.Or(
                    Builders<T>.Filter.Eq(x => x.Agent, agent),
                    Builders<T>.Filter.Eq(x => x.Agent, null))
                : Builders<T>.Filter.Eq(x => x.Agent, null);
            
            // Create a filter for either specific tenantId or null
            var tenantFilter = Builders<T>.Filter.Or(
                Builders<T>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<T>.Filter.Eq(x => x.TenantId, null)
            );
            
            // Combine the filters
            var filter = Builders<T>.Filter.And(nameFilter, agentFilter, tenantFilter);
            
            // Get all matching items
            var results = await collection.Find(filter).ToListAsync();
            
            // Client-side sort prioritizing:
            // 1. Tenant-specific items
            // 2. Items with the specified agent
            // 3. Most recent items
            return results
                .OrderByDescending(x => x.TenantId != null)
                .ThenByDescending(x => x.Agent == agent) // Prioritize exact agent match
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefault();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLatestKnowledgeByName");
    }

    public async Task<T?> GetLatestSystemByNameAsync<T>(string name, string agent) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();

            var nameFilter = Builders<T>.Filter.Eq(x => x.Name, name);
            var agentFilter = !string.IsNullOrEmpty(agent)
                ? Builders<T>.Filter.Or(
                    Builders<T>.Filter.Eq(x => x.Agent, agent),
                    Builders<T>.Filter.Eq(x => x.Agent, null))
                : Builders<T>.Filter.Eq(x => x.Agent, null);
            var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, null);
            var systemScopeFilter = Builders<T>.Filter.Eq("system_scoped", true);

            var filter = Builders<T>.Filter.And(nameFilter, agentFilter, tenantFilter, systemScopeFilter);

            var results = await collection.Find(filter).ToListAsync();

            return results
                .OrderByDescending(x => x.Agent == agent)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefault();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLatestSystemKnowledgeByName");
    }

    public async Task<T> GetByIdAsync<T>(string id) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            return await collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetKnowledgeById");
    }

    public async Task<T> GetByVersionAsync<T>(string version) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            return await collection.Find(x => x.Version == version).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetKnowledgeByVersion");
    }

    public async Task<List<T>> GetByNameAsync<T>(string name, string? agent, string tenantId) where T : IKnowledge
    {
        _logger.LogInformation("GetByNameAsync: name: {Name}, agent: {Agent}, tenantId: {TenantId}", name, agent, tenantId);
        var collection = GetTypedCollection<T>();
        
        var nameFilter = Builders<T>.Filter.Eq(x => x.Name, name);
        
        // Create an agent filter that matches either the specified agent or null agent
        var agentFilter = !string.IsNullOrEmpty(agent) 
            ? Builders<T>.Filter.Or(
                Builders<T>.Filter.Eq(x => x.Agent, agent),
                Builders<T>.Filter.Eq(x => x.Agent, null))
            : Builders<T>.Filter.Eq(x => x.Agent, null);
        
        // Create a filter for either specific tenantId or null
        var tenantFilter = Builders<T>.Filter.Or(
            Builders<T>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<T>.Filter.Eq(x => x.TenantId, null)
        );
        
        // Combine the filters
        var filter = Builders<T>.Filter.And(nameFilter, agentFilter, tenantFilter);
        
        // Get all results
        var results = await collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
            
        // Client-side sort to prioritize:
        // 1. Tenant-specific items
        // 2. Items with the specified agent
        // 3. Most recent items
        return results
            .OrderByDescending(x => x.TenantId != null)
            .ThenByDescending(x => x.Agent == agent) // Prioritize exact agent match
            .ThenByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<List<T>> GetAllAsync<T>(string tenantId) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        // Get both tenant-specific and global (null tenant) knowledge
        var filter = Builders<T>.Filter.Or(
            Builders<T>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<T>.Filter.Eq(x => x.TenantId, null)
        );
        
        // Define projection to exclude Content field
        var projection = Builders<T>.Projection.Exclude("Content");
        
        return await collection.Find(filter).Project<T>(projection).ToListAsync();
    }

    public async Task CreateAsync<T>(T knowledge) where T : IKnowledge
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            knowledge.CreatedAt = DateTime.UtcNow;
            var collection = GetTypedCollection<T>();
            await collection.InsertOneAsync(knowledge);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateKnowledge");
    }

    public async Task<bool> DeleteAsync<T>(string id) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            var result = await collection.DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteKnowledge");
    }

    public async Task<bool> UpdateAsync<T>(string id, T knowledge) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            var result = await collection.ReplaceOneAsync(x => x.Id == id, knowledge);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateKnowledge");
    }

    public async Task<T?> GetByNameVersionAsync<T>(string name, string version, string agent, string tenantId) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        // Create filters for name and version
        var nameFilter = Builders<T>.Filter.Eq(x => x.Name, name);
        var versionFilter = Builders<T>.Filter.Eq(x => x.Version, version);
        
        // Create an agent filter that matches either the specified agent or null agent
        var agentFilter = !string.IsNullOrEmpty(agent) 
            ? Builders<T>.Filter.Or(
                Builders<T>.Filter.Eq(x => x.Agent, agent),
                Builders<T>.Filter.Eq(x => x.Agent, null))
            : Builders<T>.Filter.Eq(x => x.Agent, null);
        
        // Create a filter for either specific tenantId or null
        var tenantFilter = Builders<T>.Filter.Or(
            Builders<T>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<T>.Filter.Eq(x => x.TenantId, null)
        );
        
        // Combine the filters
        var filter = Builders<T>.Filter.And(nameFilter, versionFilter, agentFilter, tenantFilter);
        
        // Get all matching results
        var results = await collection.Find(filter).ToListAsync();
        
        // Client-side sort prioritizing:
        // 1. Tenant-specific over global
        // 2. Items with the specified agent over null agent
        return results
            .OrderByDescending(x => x.TenantId != null)
            .ThenByDescending(x => x.Agent == agent) // Prioritize exact agent match
            .FirstOrDefault();
    }

    public async Task<List<T>> SearchAsync<T>(string searchTerm, string tenantId) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        var nameFilter = Builders<T>.Filter.Regex(x => x.Name, new BsonRegularExpression(searchTerm, "i"));
        var versionFilter = Builders<T>.Filter.Regex(x => x.Version, new BsonRegularExpression(searchTerm, "i"));
        
        var tenantFilter = Builders<T>.Filter.Or(
            Builders<T>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<T>.Filter.Eq(x => x.TenantId, null)
        );
        
        var filter = Builders<T>.Filter.And(
            tenantFilter,
            Builders<T>.Filter.Or(nameFilter, versionFilter)
        );

        return await collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<T>> GetUniqueLatestAsync<T>(string? tenantId, List<string> agentNames) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        // Handle case where agentNames is empty or null - return empty list
        // Repository should never return global knowledge for tenant-specific queries
        if (agentNames == null || agentNames.Count == 0)
        {
            return new List<T>();
        }
        
        // Create tenant filter - only for specific tenant, never global (null)
        var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, tenantId);
        
        // Create agent filter that matches any of the specified agents 
        var agentFilters = agentNames.Select(agent => 
                Builders<T>.Filter.Eq(x => x.Agent, agent)).ToList();
        var agentFilter = Builders<T>.Filter.Or(agentFilters);
        
        // Combine the filters
        var filter = Builders<T>.Filter.And(tenantFilter, agentFilter);
        
        // Define projection to exclude Content field
        var projection = Builders<T>.Projection.Exclude("Content");
        
        // Get all items sorted by creation date
        var allItems = await collection.Find(filter)
            .Project<T>(projection)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
            
        // Process in memory to get the latest for each name/agent combination
        return allItems
            .GroupBy(x => new { x.Name, x.Agent })
            .Select(group => group
                .OrderByDescending(x => x.CreatedAt)
                .First())
            .ToList();
    }

    public async Task<List<T>> GetUniqueLatestSystemScopedAsync<T>(List<string> agentNames) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        // Handle case where agentNames is empty or null - return empty list
        if (agentNames == null || agentNames.Count == 0)
        {
            return new List<T>();
        }
        
        // System-scoped: TenantId is null AND SystemScoped is true
        var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, null);
        var systemScopedFilter = Builders<T>.Filter.Eq("system_scoped", true);
        
        // Create agent filter that matches any of the specified agents 
        var agentFilters = agentNames.Select(agent => 
                Builders<T>.Filter.Eq(x => x.Agent, agent)).ToList();
        var agentFilter = Builders<T>.Filter.Or(agentFilters);
        
        // Combine the filters
        var filter = Builders<T>.Filter.And(tenantFilter, systemScopedFilter, agentFilter);
        
        // Define projection to exclude Content field
        var projection = Builders<T>.Projection.Exclude("Content");
        
        // Get all items sorted by creation date
        var allItems = await collection.Find(filter)
            .Project<T>(projection)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
            
        // Process in memory to get the latest for each name/agent combination
        return allItems
            .GroupBy(x => new { x.Name, x.Agent })
            .Select(group => group
                .OrderByDescending(x => x.CreatedAt)
                .First())
            .ToList();
    }

    public async Task<List<T>> GetUniqueLatestTenantScopedAsync<T>(string? tenantId, List<string> agentNames) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        // Handle case where agentNames is empty or null - return empty list
        if (agentNames == null || agentNames.Count == 0)
        {
            return new List<T>();
        }
        
        // Tenant-scoped: TenantId matches the provided tenant (not null)
        var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, tenantId);
        // Exclude system-scoped items (SystemScoped should be false or not set)
        var notSystemScopedFilter = Builders<T>.Filter.Or(
            Builders<T>.Filter.Eq("system_scoped", false),
            Builders<T>.Filter.Exists("system_scoped", false)
        );
        
        // Create agent filter that matches any of the specified agents 
        var agentFilters = agentNames.Select(agent => 
                Builders<T>.Filter.Eq(x => x.Agent, agent)).ToList();
        var agentFilter = Builders<T>.Filter.Or(agentFilters);
        
        // Combine the filters
        var filter = Builders<T>.Filter.And(tenantFilter, notSystemScopedFilter, agentFilter);
        
        // Define projection to exclude Content field
        var projection = Builders<T>.Projection.Exclude("Content");
        
        // Get all items sorted by creation date
        var allItems = await collection.Find(filter)
            .Project<T>(projection)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
            
        // Process in memory to get the latest for each name/agent combination
        return allItems
            .GroupBy(x => new { x.Name, x.Agent })
            .Select(group => group
                .OrderByDescending(x => x.CreatedAt)
                .First())
            .ToList();
    }

    public async Task<bool> DeleteAllVersionsAsync<T>(string name, string? agent, string? tenantId) where T : IKnowledge
    {
        var collection = GetTypedCollection<T>();
        
        var nameFilter = Builders<T>.Filter.Eq(x => x.Name, name);
        
        // Create agent filter
        FilterDefinition<T> agentFilter;
        if (!string.IsNullOrEmpty(agent))
        {
            agentFilter = Builders<T>.Filter.Eq(x => x.Agent, agent);
        }
        else
        {
            agentFilter = Builders<T>.Filter.Eq(x => x.Agent, null);
        }
        
        // Add tenant filter - when tenantId is null, only delete system-scoped knowledge
        var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, tenantId);
        
        var filter = Builders<T>.Filter.And(nameFilter, agentFilter, tenantFilter);
        
        var result = await collection.DeleteManyAsync(filter);
        return result.DeletedCount > 0;
    }

    public async Task<List<T>> GetSystemScopedByAgentAsync<T>(string agentName) where T : IKnowledge
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var collection = GetTypedCollection<T>();
            
            // Filter for system-scoped knowledge (TenantId is null) associated with the specific agent
            var agentFilter = Builders<T>.Filter.Eq(x => x.Agent, agentName);
            var tenantFilter = Builders<T>.Filter.Eq(x => x.TenantId, null);
            
            var filter = Builders<T>.Filter.And(agentFilter, tenantFilter);
            
            // Get all system-scoped knowledge for this agent
            var allKnowledge = await collection.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
            
            // Get the latest version of each unique knowledge name
            return allKnowledge
                .GroupBy(x => x.Name)
                .Select(group => group
                    .OrderByDescending(x => x.CreatedAt)
                    .First())
                .ToList();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetSystemScopedByAgent");
    }

    private IMongoCollection<T> GetTypedCollection<T>() where T : IKnowledge
    {
        return _knowledge.Database.GetCollection<T>("knowledge");
    }
} 