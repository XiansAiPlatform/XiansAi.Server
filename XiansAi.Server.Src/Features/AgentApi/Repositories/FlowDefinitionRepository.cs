using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Features.AgentApi.Repositories;

public interface IFlowDefinitionRepository
{
    Task<FlowDefinition?> GetByWorkflowTypeAsync(string workflowType, bool systemScoped, string? tenant);
    Task CreateAsync(FlowDefinition definition);
    Task<bool> DeleteAsync(string id);
}

public class FlowDefinitionRepository : IFlowDefinitionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;

    public FlowDefinitionRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
    }


    public async Task<FlowDefinition?> GetByWorkflowTypeAsync(string workflowType, bool systemScoped, string? tenant)
    {
        var filters = new List<FilterDefinition<FlowDefinition>>
        {
            Builders<FlowDefinition>.Filter.Eq(x => x.WorkflowType, workflowType)
        };

        if (systemScoped)
        {
            filters.Add(Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, true));
        }
        else
        {
            // Handle null tenant explicitly for system-scoped agents
            var tenantFilter = tenant == null
                ? Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, null)
                : Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, tenant);
            filters.Add(tenantFilter);
        }

        var filter = Builders<FlowDefinition>.Filter.And(filters);
        
        return await _definitions.Find(filter).FirstOrDefaultAsync();
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
} 