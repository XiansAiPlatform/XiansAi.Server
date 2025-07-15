using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface ITenantRepository
{
    Task<Tenant> GetByIdAsync(string id);
    Task<Tenant> GetByTenantIdAsync(string tenantId);
    Task<Tenant> GetByDomainAsync(string domain);
    Task<List<Tenant>> GetAllAsync(string? tenantId = null);
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

    public TenantRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _collection = database.GetCollection<Tenant>("tenants");
    }

    // Standard CRUD Operations
    public async Task<Tenant> GetByIdAsync(string id)
    {
        return await _collection.Find(tenant => tenant.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Tenant> GetByTenantIdAsync(string tenantId)
    {
        return await _collection.Find(tenant => tenant.TenantId == tenantId).FirstOrDefaultAsync();
    }

    public async Task<Tenant> GetByDomainAsync(string domain)
    {
        return await _collection.Find(tenant => tenant.Domain == domain).FirstOrDefaultAsync();
    }

    public async Task<List<Tenant>> GetAllAsync(string? tenantId = null)
    {
        // Tenant admin: return only their own tenant
        if (tenantId != null)
        {
            var tenant = await _collection.Find(t => t.TenantId == tenantId).FirstOrDefaultAsync();
            return tenant != null ? new List<Tenant> { tenant } : new List<Tenant>();
        }

        return await _collection.Find(_ => true).ToListAsync();
    }

    public async Task CreateAsync(Tenant tenant)
    {
        await _collection.InsertOneAsync(tenant);
    }

    public async Task<bool> UpdateAsync(string id, Tenant tenant)
    {
        tenant.UpdatedAt = DateTime.UtcNow;
        var result = await _collection.ReplaceOneAsync(t => t.Id == id, tenant);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _collection.DeleteOneAsync(tenant => tenant.Id == id);
        return result.DeletedCount > 0;
    }

    // Advanced Query Methods
    public async Task<List<Tenant>> SearchAsync(string searchTerm)
    {
        var filter = Builders<Tenant>.Filter.Or(
            Builders<Tenant>.Filter.Regex(x => x.Name, new BsonRegularExpression(searchTerm, "i")),
            Builders<Tenant>.Filter.Regex(x => x.Domain, new BsonRegularExpression(searchTerm, "i")),
            Builders<Tenant>.Filter.Regex(x => x.Description, new BsonRegularExpression(searchTerm, "i"))
        );
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<List<Tenant>> GetTenantsByCreatorAsync(string createdBy)
    {
        return await _collection.Find(tenant => tenant.CreatedBy == createdBy).ToListAsync();
    }

    public async Task<bool> AddAgentAsync(string tenantId, Agent agent)
    {
        var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
        var update = Builders<Tenant>.Update
            .Push(t => t.Agents, agent)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateAgentAsync(string tenantId, string agentName, Agent updatedAgent)
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
    }

    public async Task<bool> RemoveAgentAsync(string tenantId, string agentName)
    {
        var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
        var update = Builders<Tenant>.Update
            .PullFilter(t => t.Agents, a => a.Name == agentName)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> AddFlowToAgentAsync(string tenantId, string agentName, Flow flow)
    {
        var tenant = await GetByIdAsync(tenantId);
        if (tenant == null) return false;

        var agent = tenant.Agents?.FirstOrDefault(a => a.Name == agentName);
        if (agent == null) return false;


        return await UpdateAsync(tenantId, tenant);
    }
}