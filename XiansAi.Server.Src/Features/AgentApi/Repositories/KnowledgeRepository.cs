using MongoDB.Driver;
using Shared.Data.Models;
using XiansAi.Server.Shared.Data;

namespace Features.AgentApi.Repositories;

public interface IKnowledgeRepository
{
    Task<Knowledge> GetLatestKnowledgeByNameAsync(string name);
    Task<Knowledge> GetByIdAsync(string id);
    Task<Knowledge> GetByVersionAsync(string version);
    Task<Knowledge> GetLatestByNameAsync(string name);
}

public class KnowledgeRepository : IKnowledgeRepository
{
    private readonly IMongoCollection<Knowledge> _knowledge;
    public KnowledgeRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
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