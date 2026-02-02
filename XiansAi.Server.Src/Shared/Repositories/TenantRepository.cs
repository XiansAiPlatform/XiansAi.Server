using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITenantRepository
{
    Task<Tenant> GetByIdAsync(string id);
    Task<Tenant> GetByTenantIdAsync(string tenantId);
    Task<Tenant> GetByDomainAsync(string domain);
    Task<List<Tenant>> GetByDomainListAsync(string domain);
    Task<List<Tenant>> GetByTenantIdsAsync(IEnumerable<string> tenantIds);
    Task<List<Tenant>> GetAllAsync();
    Task CreateAsync(Tenant tenant);
    Task<bool> UpdateAsync(string id, Tenant tenant);
    Task<bool> DeleteAsync(string id);
    Task<List<Tenant>> SearchAsync(string searchTerm);
    Task<List<Tenant>> GetTenantsByCreatorAsync(string createdBy);
    Task<bool> AddAgentAsync(string tenantId, Agent agent);
    Task<bool> UpdateAgentAsync(string tenantId, string agentName, Agent updatedAgent);
    Task<bool> RemoveAgentAsync(string tenantId, string agentName);
    Task<bool> AddFlowToAgentAsync(string tenantId, string agentName, Flow flow);
}

public class TenantRepository : ITenantRepository
{
    private readonly IMongoCollection<Tenant> _collection;
    private readonly ILogger<TenantRepository> _logger;

    public TenantRepository(IDatabaseService databaseService, ILogger<TenantRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Tenant>("tenants");
        _logger = logger;
    }

    // Standard CRUD Operations
    public async Task<Tenant> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantById");
    }

    public async Task<Tenant> GetByTenantIdAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.TenantId == tenantId).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByTenantId");
    }

    public async Task<Tenant> GetByDomainAsync(string domain)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.Domain == domain).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantByDomain");
    }

    public async Task<List<Tenant>> GetByDomainListAsync(string domain)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.And(
                Builders<Tenant>.Filter.Ne(t => t.Domain, null),
                Builders<Tenant>.Filter.Ne(t => t.Domain, string.Empty),
                Builders<Tenant>.Filter.Eq(t => t.Domain, domain)
            );
            return await _collection.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByDomain");
    }

    public async Task<List<Tenant>> GetByTenantIdsAsync(IEnumerable<string> tenantIds)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.In(t => t.TenantId, tenantIds);
            return await _collection.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByTenantIds");
    }

    public async Task<List<Tenant>> GetAllAsync()
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(_ => true).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllTenants");
    }


    public async Task CreateAsync(Tenant tenant)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.InsertOneAsync(tenant);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateTenant");
    }

    public async Task<bool> UpdateAsync(string id, Tenant tenant)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            tenant.UpdatedAt = DateTime.UtcNow;
            var result = await _collection.ReplaceOneAsync(t => t.Id == id, tenant);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateTenant");
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _collection.DeleteOneAsync(tenant => tenant.Id == id);
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteTenant");
    }

    // Advanced Query Methods
    public async Task<List<Tenant>> SearchAsync(string searchTerm)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.Or(
                Builders<Tenant>.Filter.Regex(x => x.Name, new BsonRegularExpression(searchTerm, "i")),
                Builders<Tenant>.Filter.Regex(x => x.Domain, new BsonRegularExpression(searchTerm, "i")),
                Builders<Tenant>.Filter.Regex(x => x.Description, new BsonRegularExpression(searchTerm, "i"))
            );
            return await _collection.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "SearchTenants");
    }

    public async Task<List<Tenant>> GetTenantsByCreatorAsync(string createdBy)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.CreatedBy == createdBy).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByCreator");
    }

    public async Task<bool> AddAgentAsync(string tenantId, Agent agent)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
            var update = Builders<Tenant>.Update
                .Push(t => t.Agents, agent)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddAgent");
    }

    public async Task<bool> UpdateAgentAsync(string tenantId, string agentName, Agent updatedAgent)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.And(
                Builders<Tenant>.Filter.Eq(t => t.Id, tenantId),
                Builders<Tenant>.Filter.ElemMatch(t => t.Agents, a => a.Name == agentName)
            );

            var update = Builders<Tenant>.Update
                .Set("agents.$", updatedAgent)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAgent");
    }

    public async Task<bool> RemoveAgentAsync(string tenantId, string agentName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
            var update = Builders<Tenant>.Update
                .PullFilter(t => t.Agents, a => a.Name == agentName)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "RemoveAgent");
    }

    public async Task<bool> AddFlowToAgentAsync(string tenantId, string agentName, Flow flow)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var tenant = await GetByIdAsync(tenantId);
            if (tenant == null) return false;

            var agent = tenant.Agents?.FirstOrDefault(a => a.Name == agentName);
            if (agent == null) return false;


            return await UpdateAsync(tenantId, tenant);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddFlowToAgent");
    }
}