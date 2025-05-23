using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using XiansAi.Server.Shared.Data;

namespace Features.AgentApi.Repositories;

public interface IFlowDefinitionRepository
{
    Task<FlowDefinition?> GetByWorkflowTypeAsync(string workflowType);
    Task<bool> UpdateAsync(string id, FlowDefinition definition);
    Task CreateAsync(FlowDefinition definition);
    Task<bool> DeleteAsync(string id);
}

public class FlowDefinitionRepository : IFlowDefinitionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;

    public FlowDefinitionRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
    }

    public async Task<FlowDefinition?> GetByWorkflowTypeAsync(string workflowType)
    {
        return await _definitions.Find(x => x.WorkflowType == workflowType).FirstOrDefaultAsync();
    }

    public async Task CreateAsync(FlowDefinition definition)
    {
        definition.CreatedAt = DateTime.UtcNow;
        await _definitions.InsertOneAsync(definition);
    }

    public async Task<bool> UpdateAsync(string id, FlowDefinition definition)
    {
        var result = await _definitions.ReplaceOneAsync(x => x.Id == id, definition);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _definitions.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
} 