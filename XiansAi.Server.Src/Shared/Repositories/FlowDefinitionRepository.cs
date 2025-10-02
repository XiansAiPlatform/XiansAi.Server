using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;

namespace Shared.Repositories;

public interface IFlowDefinitionRepository
{
    //Task<bool> IfExistsInAnotherOwner(string typeName, string owner);
    //Task<long> DeleteByOwnerAndTypeNameAsync(string owner, string typeName);
    Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType);
    Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType, string? tenant);
    Task<FlowDefinition> GetByIdAsync(string id);
    Task<FlowDefinition> GetByHashAsync(string hash, string workflowType);
    Task<FlowDefinition> GetByHashAsync(string hash, string workflowType, string? tenant);
    Task<List<FlowDefinition>> GetByNameAsync(string agentName);
    Task<List<FlowDefinition>> GetByNameAsync(string agentName, string? tenant);
    Task<List<FlowDefinition>> GetAllAsync();
    Task<List<FlowDefinition>> GetAllAsync(string? tenant);
    Task CreateAsync(FlowDefinition definition);
    Task<bool> DeleteAsync(string id);
    Task<long> DeleteByAgentAsync(string agentName, string? tenant);
    Task<bool> UpdateAsync(string id, FlowDefinition definition);
    Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash);
    Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash, string? tenant);
}

public class FlowDefinitionRepository : IFlowDefinitionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<FlowDefinitionRepository> _logger;

    public FlowDefinitionRepository(IDatabaseService databaseService, IAgentRepository agentRepository, ILogger<FlowDefinitionRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
        _agentRepository = agentRepository;
        _logger = logger;
    }

    public async Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType)
    {
        return await _definitions.Find(x => x.WorkflowType == workflowType)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType, string? tenant)
    {
        var filter = Builders<FlowDefinition>.Filter.And(
            Builders<FlowDefinition>.Filter.Eq(x => x.WorkflowType, workflowType),
            tenant == null 
                ? Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                : Builders<FlowDefinition>.Filter.Or(
                    Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
                    Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                )
        );

        return await _definitions.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByIdAsync(string id)
    {
        return await _definitions.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByHashAsync(string hash, string workflowType)
    {
        return await _definitions.Find(x => x.Hash == hash && x.WorkflowType == workflowType).FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByHashAsync(string hash, string workflowType, string? tenant)
    {
        var filter = Builders<FlowDefinition>.Filter.And(
            Builders<FlowDefinition>.Filter.Eq(x => x.Hash, hash),
            Builders<FlowDefinition>.Filter.Eq(x => x.WorkflowType, workflowType),
            tenant == null 
                ? Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                : Builders<FlowDefinition>.Filter.Or(
                    Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
                    Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                )
        );

        return await _definitions.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<FlowDefinition>> GetByNameAsync(string agentName)
    {
        return await _definitions.Find(x => x.Agent == agentName)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetByNameAsync(string agentName, string? tenant)
    {
        var filter = Builders<FlowDefinition>.Filter.And(
            Builders<FlowDefinition>.Filter.Eq(x => x.Agent, agentName),
            tenant == null 
                ? Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                : Builders<FlowDefinition>.Filter.Or(
                    Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
                    Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                )
        );

        return await _definitions.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetAllAsync()
    {
        return await _definitions.Find(_ => true).ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetAllAsync(string? tenant)
    {
        var filter = tenant == null 
            ? Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
            : Builders<FlowDefinition>.Filter.Or(
                Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
                Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
            );

        return await _definitions.Find(filter).ToListAsync();
    }

    public async Task CreateAsync(FlowDefinition definition)
    {
        definition.CreatedAt = DateTime.UtcNow;
        await _definitions.InsertOneAsync(definition);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _definitions.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<long> DeleteByAgentAsync(string agentName, string? tenant)
    {
        _logger.LogInformation("Deleting all flow definitions for agent: {AgentName} in tenant: {Tenant}", agentName, tenant);
        
        // Build filter to match agent name, tenant, and system scoped value
        var filter = Builders<FlowDefinition>.Filter.And(
            Builders<FlowDefinition>.Filter.Eq(x => x.Agent, agentName),
            Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
            Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, tenant == null)
        );
        
        var result = await _definitions.DeleteManyAsync(filter);
        _logger.LogInformation("Deleted {Count} flow definitions for agent: {AgentName} in tenant: {Tenant}", result.DeletedCount, agentName, tenant);
        return result.DeletedCount;
    }

    public async Task<bool> UpdateAsync(string id, FlowDefinition definition)
    {
        var result = await _definitions.ReplaceOneAsync(x => x.Id == id, definition);
        return result.ModifiedCount > 0;
    }

    public async Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash)
    {
        return await _definitions.Find(x => x.WorkflowType == workflowType && x.Hash == hash)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash, string? tenant)
    {
        var filter = Builders<FlowDefinition>.Filter.And(
            Builders<FlowDefinition>.Filter.Eq(x => x.WorkflowType, workflowType),
            Builders<FlowDefinition>.Filter.Eq(x => x.Hash, hash),
            tenant == null 
                ? Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                : Builders<FlowDefinition>.Filter.Or(
                    Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant),
                    Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true)
                )
        );

        return await _definitions.Find(filter).FirstOrDefaultAsync();
    }


}


