using MongoDB.Bson;
using MongoDB.Driver;
using XiansAi.Server.Features.WebApi.Models;
namespace XiansAi.Server.Features.WebApi.Repositories;

public class InstructionRepository
{
    private readonly IMongoCollection<Instruction> _instructions;

    public InstructionRepository(IMongoDatabase database)
    {
        _instructions = database.GetCollection<Instruction>("knowledge");
    }

    public async Task<Instruction> GetLatestInstructionByNameAsync(string name)
    {
        return await _instructions.Find(x => x.Name == name)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<Instruction> GetByIdAsync(string id)
    {
        return await _instructions.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Instruction> GetByVersionAsync(string version)
    {
        return await _instructions.Find(x => x.Version == version).FirstOrDefaultAsync();
    }

    public async Task<List<Instruction>> GetByNameAsync(string name)
    {
        return await _instructions.Find(x => x.Name == name)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<Instruction> GetLatestByNameAsync(string name)
    {
        return await _instructions.Find(x => x.Name == name)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Instruction>> GetAllAsync()
    {
        return await _instructions.Find(_ => true).ToListAsync();
    }

    public async Task CreateAsync(Instruction instruction)
    {
        instruction.CreatedAt = DateTime.UtcNow;
        await _instructions.InsertOneAsync(instruction);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _instructions.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<bool> UpdateAsync(string id, Instruction instruction)
    {
        var result = await _instructions.ReplaceOneAsync(x => x.Id == id, instruction);
        return result.ModifiedCount > 0;
    }

    public async Task<Instruction> GetByNameVersionAsync(string name, string version)
    {
        return await _instructions.Find(x => x.Name == name && x.Version == version)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Instruction>> SearchAsync(string searchTerm)
    {
        var filter = Builders<Instruction>.Filter.Or(
            Builders<Instruction>.Filter.Regex(x => x.Name, new BsonRegularExpression(searchTerm, "i")),
            Builders<Instruction>.Filter.Regex(x => x.Version, new BsonRegularExpression(searchTerm, "i"))
        );

        return await _instructions.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Instruction>> GetUniqueLatestInstructionsAsync()
    {
        return await _instructions.Aggregate()
            .SortByDescending(x => x.CreatedAt)
            .Group(x => x.Name, g => g.First())
            .ToListAsync();
    }

    public async Task<bool> DeleteAllVersionsAsync(string name)
    {
        var result = await _instructions.DeleteManyAsync(x => x.Name == name);
        return result.DeletedCount > 0;
    }
}


