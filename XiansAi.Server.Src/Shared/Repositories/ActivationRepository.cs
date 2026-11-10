using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface IActivationRepository
{
    Task<AgentActivation?> GetByIdAsync(string id);
    Task<AgentActivation?> GetByNameAndAgentAsync(string tenantId, string agentName, string activationName);
    Task<List<AgentActivation>> GetByTenantIdAsync(string tenantId);
    Task<List<AgentActivation>> GetByAgentNameAsync(string agentName, string tenantId);
    Task<List<AgentActivation>> GetActiveActivationsAsync(string tenantId);
    /// <summary>
    /// Counts activations per agent name in a tenant. When <paramref name="agentNames"/> is null or empty,
    /// every agent with at least one activation is counted. Agents with no activations are not included.
    /// </summary>
    Task<Dictionary<string, AgentActivationCount>> CountByAgentNamesAsync(string tenantId, IReadOnlyCollection<string>? agentNames);
    Task CreateAsync(AgentActivation activation);
    Task<bool> UpdateAsync(string id, AgentActivation activation);
    Task<bool> DeleteAsync(string id);
}

/// <summary>Number of activations for one agent: all of them, and those currently active.</summary>
public readonly record struct AgentActivationCount(long Total, long Active);

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

    public async Task<AgentActivation?> GetByNameAndAgentAsync(string tenantId, string agentName, string activationName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<AgentActivation>.Filter.And(
                Builders<AgentActivation>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AgentActivation>.Filter.Eq(x => x.AgentName, agentName),
                Builders<AgentActivation>.Filter.Eq(x => x.Name, activationName));
            return await _activations.Find(filter).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActivationByNameAndAgent");
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
            var filter = Builders<AgentActivation>.Filter.And(
                Builders<AgentActivation>.Filter.Eq(x => x.TenantId, tenantId),
                IsActiveFilter());
            return await _activations.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetActiveActivations");
    }

    public async Task<Dictionary<string, AgentActivationCount>> CountByAgentNamesAsync(
        string tenantId,
        IReadOnlyCollection<string>? agentNames)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var baseFilter = Builders<AgentActivation>.Filter.Eq(x => x.TenantId, tenantId);
            if (agentNames is { Count: > 0 })
            {
                baseFilter &= Builders<AgentActivation>.Filter.In(x => x.AgentName, agentNames);
            }

            var totals = await CountGroupedByAgentNameAsync(baseFilter);
            var actives = await CountGroupedByAgentNameAsync(baseFilter & IsActiveFilter());

            return totals.ToDictionary(
                kv => kv.Key,
                kv => new AgentActivationCount(kv.Value, actives.GetValueOrDefault(kv.Key)));
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CountActivationsByAgentNames");
    }

    private async Task<Dictionary<string, long>> CountGroupedByAgentNameAsync(FilterDefinition<AgentActivation> filter)
    {
        var groups = await _activations.Aggregate()
            .Match(filter)
            .Group(x => x.AgentName, g => new { AgentName = g.Key, Count = g.LongCount() })
            .ToListAsync();
        return groups.ToDictionary(g => g.AgentName, g => g.Count);
    }

    /// <summary>
    /// Active == true for new documents, or inferred from ActivatedAt/DeactivatedAt for legacy documents.
    /// Mirrors <see cref="AgentActivation.IsActive"/>.
    /// </summary>
    private static FilterDefinition<AgentActivation> IsActiveFilter() =>
        Builders<AgentActivation>.Filter.Or(
            Builders<AgentActivation>.Filter.Eq(x => x.Active, true),
            Builders<AgentActivation>.Filter.And(
                Builders<AgentActivation>.Filter.Ne(x => x.ActivatedAt, null),
                Builders<AgentActivation>.Filter.Eq(x => x.DeactivatedAt, null),
                Builders<AgentActivation>.Filter.Or(
                    Builders<AgentActivation>.Filter.Eq(x => x.Active, (bool?)null),
                    Builders<AgentActivation>.Filter.Exists("active", false))));

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
