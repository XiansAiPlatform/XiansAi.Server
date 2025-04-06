using MongoDB.Driver;
using Shared.Data.Models;

namespace Features.AgentApi.Repositories;

public class KnowledgeRepository
{
    private readonly IMongoCollection<Knowledge> _knowledge;

    public KnowledgeRepository(IMongoDatabase database)
    {
        _knowledge = database.GetCollection<Knowledge>("knowledge");
    }

    public async Task<Knowledge> GetLatestKnowledgeByNameAsync(string name)
    {
        return await _knowledge.Find(x => x.Name == name)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Knowledge> GetByIdAsync(string id)
    {
        return await _knowledge.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Knowledge> GetByVersionAsync(string version)
    {
        return await _knowledge.Find(x => x.Version == version).FirstOrDefaultAsync();
    }
    public async Task<Knowledge> GetLatestByNameAsync(string name)
    {
        return await _knowledge.Find(x => x.Name == name)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

} 