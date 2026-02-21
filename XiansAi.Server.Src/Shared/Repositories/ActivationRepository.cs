using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface IActivationRepository
{
    Task<AgentActivation?> GetByIdAsync(string id);
    Task<List<AgentActivation>> GetByTenantIdAsync(string tenantId);
    Task<List<AgentActivation>> GetByAgentNameAsync(string agentName, string tenantId);
    Task<List<AgentActivation>> GetActiveActivationsAsync(string tenantId);
    Task CreateAsync(AgentActivation activation);
    Task<bool> UpdateAsync(string id, AgentActivation activation);
    Task<bool> DeleteAsync(string id);
}

public class ActivationRepository : IActivationRepository
{
    private readonly IMongoCollection<AgentActivation> _activations;
    private readonly ILogger<ActivationRepository> _logger;

    public ActivationRepository(IDatabaseService databaseService, ILogger<ActivationRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _activations = database.GetCollection<AgentActivation>("activations");
        _logger = logger;
    }

    public async Task<AgentActivation?> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            if (!ObjectId.TryParse(id, out _))
            {
                return null;
            }
            return await _activations.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActivationById");
    }

    public async Task<List<AgentActivation>> GetByTenantIdAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _activations.Find(x => x.TenantId == tenantId).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActivationsByTenantId");
    }

    public async Task<List<AgentActivation>> GetByAgentNameAsync(string agentName, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _activations.Find(x => x.AgentName == agentName && x.TenantId == tenantId).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActivationsByAgentName");
    }

    public async Task<List<AgentActivation>> GetActiveActivationsAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Active == true for new documents, or infer from ActivatedAt/DeactivatedAt for legacy documents
            var filter = Builders<AgentActivation>.Filter.And(
                Builders<AgentActivation>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AgentActivation>.Filter.Or(
                    Builders<AgentActivation>.Filter.Eq(x => x.Active, true),
                    Builders<AgentActivation>.Filter.And(
                        Builders<AgentActivation>.Filter.Ne(x => x.ActivatedAt, null),
                        Builders<AgentActivation>.Filter.Eq(x => x.DeactivatedAt, null),
                        Builders<AgentActivation>.Filter.Or(
                            Builders<AgentActivation>.Filter.Eq(x => x.Active, (bool?)null),
                            Builders<AgentActivation>.Filter.Exists("active", false)))
                ));
            return await _activations.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActiveActivations");
    }

    public async Task CreateAsync(AgentActivation activation)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            activation.CreatedAt = DateTime.UtcNow;
            await _activations.InsertOneAsync(activation);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateActivation");
    }

    public async Task<bool> UpdateAsync(string id, AgentActivation activation)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _activations.ReplaceOneAsync(x => x.Id == id, activation);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateActivation");
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _activations.DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteActivation");
    }
}
