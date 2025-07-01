using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;

namespace Shared.Repositories;

public interface IFlowDefinitionRepository
{
    //Task<bool> IfExistsInAnotherOwner(string typeName, string owner);
    //Task<long> DeleteByOwnerAndTypeNameAsync(string owner, string typeName);
    Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType);
    Task<FlowDefinition> GetByIdAsync(string id);
    Task<FlowDefinition> GetByHashAsync(string hash, string workflowType);
    Task<List<FlowDefinition>> GetByNameAsync(string agentName);
    Task<List<FlowDefinition>> GetAllAsync();
    Task CreateAsync(FlowDefinition definition);
    Task<bool> DeleteAsync(string id);
    Task<long> DeleteByAgentAsync(string agentName);
    Task<bool> UpdateAsync(string id, FlowDefinition definition);
    Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash);
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

    public async Task<FlowDefinition> GetByIdAsync(string id)
    {
        return await _definitions.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByHashAsync(string hash, string workflowType)
    {
        return await _definitions.Find(x => x.Hash == hash && x.WorkflowType == workflowType).FirstOrDefaultAsync();
    }

    public async Task<List<FlowDefinition>> GetByNameAsync(string agentName)
    {
        return await _definitions.Find(x => x.Agent == agentName)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetAllAsync()
    {
        return await _definitions.Find(_ => true).ToListAsync();
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

    public async Task<long> DeleteByAgentAsync(string agentName)
    {
        _logger.LogInformation("Deleting all flow definitions for agent: {AgentName}", agentName);
        var result = await _definitions.DeleteManyAsync(x => x.Agent == agentName);
        _logger.LogInformation("Deleted {Count} flow definitions for agent: {AgentName}", result.DeletedCount, agentName);
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


}


