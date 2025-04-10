using MongoDB.Driver;
using MongoDB.Bson;
using XiansAi.Server.Database.Models;

namespace XiansAi.Server.Database.Repositories;

public class LogRepository
{
    private readonly IMongoCollection<Log> _logs;

    public LogRepository(IMongoDatabase database)
    {
        _logs = database.GetCollection<Log>("logs");
    }

    public async Task<Log?> GetByIdAsync(string id)
    {
        return await _logs.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<Log>> GetByTenantIdAsync(string tenantId)
    {
        return await _logs.Find(x => x.TenantId == tenantId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Log>> GetByWorkflowIdAsync(string workflowId)
    {
        return await _logs.Find(x => x.WorkflowId == workflowId)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit)
    {
        var Logs = await _logs.Find(x => x.WorkflowRunId == workflowRunId)
            .SortByDescending(x => x.CreatedAt)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();
        return Logs;
    }

    public async Task<List<Log>> GetByLogLevelAsync(Models.LogLevel level)
    {
        return await _logs.Find(x => x.Level == level)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Log>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _logs.Find(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task CreateAsync(Log log)
    {
        await _logs.InsertOneAsync(log);
    }

    public async Task<bool> UpdateAsync(string id, Log log)
    {
        var result = await _logs.ReplaceOneAsync(x => x.Id == id, log);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdatePropertiesAsync(string id, Dictionary<string, object> properties)
    {
        var update = Builders<Log>.Update.Set(x => x.Properties, properties);
        var result = await _logs.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _logs.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<List<Log>> SearchAsync(string searchTerm)
    {
        var filter = Builders<Log>.Filter.Regex(x => x.Message, new BsonRegularExpression(searchTerm, "i"));

        return await _logs.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
