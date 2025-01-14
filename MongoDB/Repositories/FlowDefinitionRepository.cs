using MongoDB.Bson;
using MongoDB.Driver;
using XiansAi.Server.MongoDB.Models;

namespace XiansAi.Server.MongoDB.Repositories;

public class FlowDefinitionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;

    public FlowDefinitionRepository(IMongoDatabase database)
    {
        _definitions = database.GetCollection<FlowDefinition>("definitions");
    }

    public async Task<bool> IfExistsInAnotherOwner(string typeName, string owner)
    {
        var existingDefinition = await _definitions.Find(x => x.TypeName == typeName && x.Owner != owner).FirstOrDefaultAsync();
        return existingDefinition != null;
    }

    public async Task<FlowDefinition> GetLatestFlowDefinitionAsync(string typeName)
    {
        return await _definitions.Find(x => x.TypeName == typeName)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByIdAsync(string id)
    {
        return await _definitions.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByHashAsync(string hash, string typeName)
    {
        return await _definitions.Find(x => x.Hash == hash && x.TypeName == typeName).FirstOrDefaultAsync();
    }

    public async Task<List<FlowDefinition>> GetByNameAsync(string name)
    {
        return await _definitions.Find(x => x.TypeName == name)
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

    public async Task<bool> UpdateAsync(string id, FlowDefinition definition)
    {
        var result = await _definitions.ReplaceOneAsync(x => x.Id == id, definition);
        return result.ModifiedCount > 0;
    }

    public async Task<FlowDefinition> GetByNameHashAsync(string typeName, string hash)
    {
        return await _definitions.Find(x => x.TypeName == typeName && x.Hash == hash)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetLatestByClassAndOwnerAsync(string className, string owner)
    {
        return await _definitions.Find(x => x.ClassName == className && x.Owner == owner)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<FlowDefinition>> GetLatestDefinitionsAsync()
    {
        return await _definitions.Aggregate()
            .SortByDescending(x => x.CreatedAt)
            .Group(x => new { x.ClassName, x.Owner },
                   g => g.First())
            .ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetLatestDefinitionsForAllTypesAsync()
    {
        return await _definitions.Aggregate()
            .SortByDescending(x => x.CreatedAt)
            .Group(x => new { x.ClassName, x.Owner },
                   g => g.First())
            .ToListAsync();
    }
}


