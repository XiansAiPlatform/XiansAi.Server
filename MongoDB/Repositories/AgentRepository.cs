using MongoDB.Bson;
using MongoDB.Driver;
using XiansAi.Server.MongoDB.Models;

namespace XiansAi.Server.MongoDB.Repositories;

public class AgentRepository
{
    private readonly IMongoCollection<Agent> _agents;

    public AgentRepository(IMongoDatabase database)
    {
        _agents = database.GetCollection<Agent>("agents");
    }

    public async Task<Agent?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var objectId))
        {
            return null;
        }

        return await _agents.Find(x => x.Id == objectId).FirstOrDefaultAsync();
    }

    public async Task<Agent?> GetByIdAsync(ObjectId id)
    {
        return await _agents.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Agent> CreateAsync(Agent agent)
    {
        agent.Id = ObjectId.GenerateNewId();
        agent.CreatedAt = agent.Id.CreationTime; // Using ObjectId's timestamp
        await _agents.InsertOneAsync(agent);
        return agent;
    }
} 