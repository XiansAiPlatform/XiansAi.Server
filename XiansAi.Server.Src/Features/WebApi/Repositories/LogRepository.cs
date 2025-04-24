using MongoDB.Driver;
using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Shared.Data;

namespace XiansAi.Server.Features.WebApi.Repositories;

public interface ILogRepository
{
    Task<Log?> GetByIdAsync(string id);
    Task<List<Log>> GetByTenantIdAsync(string tenantId);
    Task<List<Log>> GetByWorkflowIdAsync(string workflowId);
    Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null);
    Task<List<Log>> GetByLogLevelAsync(Models.LogLevel level);
    Task<List<Log>> GetLastLogAsync(DateTime? startTime, DateTime? endTime);
    Task<List<Log>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task CreateAsync(Log log);
    Task<bool> UpdateAsync(string id, Log log);
    Task<bool> UpdatePropertiesAsync(string id, Dictionary<string, object> properties);
    Task<bool> DeleteAsync(string id);
    Task<List<Log>> SearchAsync(string searchTerm);
}

public class LogRepository : ILogRepository
{
    private readonly IMongoCollection<Log> _logs;

    public LogRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().Result;
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

    public async Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.WorkflowRunId, workflowRunId);
        if (logLevel.HasValue)
        {
            filter &= Builders<Log>.Filter.Eq(x => x.Level, (Models.LogLevel)logLevel.Value);
        }

        // Count total logs
        var totalCount = (int)await _logs.CountDocumentsAsync(filter);
        
        // If skip exceeds total count, return empty list
        if (skip >= totalCount)
        {
            return new List<Log>();
        }
        
        // Handle edge case where skip+limit exceeds total count
        if (skip + limit > totalCount)
        {
            limit = Math.Max(0, totalCount - skip);
        }
        
        // If skip is from the beginning, convert it to skip from the end
        var adjustedSkip = Math.Max(0, totalCount - limit - skip);
        var adjustedLimit = Math.Min(limit, totalCount - adjustedSkip);

        // Get logs with the adjusted skip/limit
        var logs = await _logs.Find(filter)
            .Skip(adjustedSkip)
            .Limit(adjustedLimit)
            .ToListAsync();
            
        // Reverse to get newest first
        logs.Reverse();
        return logs;
    }

    public async Task<List<Log>> GetByLogLevelAsync(Models.LogLevel level)
    {
        return await _logs.Find(x => x.Level == level)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Log>> GetLastLogAsync(DateTime? startTime, DateTime? endTime)
    {
        var matchStage = Builders<Log>.Filter.Empty;

        if (startTime.HasValue)
            matchStage &= Builders<Log>.Filter.Gte(x => x.CreatedAt, startTime.Value);

        if (endTime.HasValue)
            matchStage &= Builders<Log>.Filter.Lte(x => x.CreatedAt, endTime.Value);

        return await _logs.Aggregate()
            .Match(matchStage)
            .Group(x => x.WorkflowRunId, g => g.Last())
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
