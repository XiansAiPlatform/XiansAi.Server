using Features.WebApi.Models;
using MongoDB.Driver;
using Shared.Data;

namespace Features.WebApi.Repositories;

public interface ILogRepository
{
    Task<Log?> GetByIdAsync(string id);
    Task<List<Log>> GetByTenantIdAsync(string tenantId);
    Task<List<Log>> GetByWorkflowIdAsync(string workflowId);
    Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null);
    Task<List<Log>> GetByLogLevelAsync(LogLevel level);
    Task<List<Log>> GetLastLogAsync(DateTime? startTime, DateTime? endTime);
    Task<List<Log>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<bool> UpdateAsync(string id, Log log);
    Task<bool> UpdatePropertiesAsync(string id, Dictionary<string, object> properties);
    Task<bool> DeleteAsync(string id);
    
    // New optimized methods
    Task<(IEnumerable<string?> participants, long totalCount)> GetDistinctParticipantsForAgentAsync(string tenantId, string agent, int page = 1, int pageSize = 20);
    Task<IEnumerable<string>> GetDistinctWorkflowTypesAsync(string tenantId, string agent, string? participantId = null);
    Task<IEnumerable<string>> GetDistinctWorkflowIdsForTypeAsync(string tenantId, string agent, string workflowType, string? participantId = null);
    Task<(IEnumerable<Log> logs, long totalCount)> GetFilteredLogsAsync(
        string tenantId,
        string agent,
        string? participantId,
        string? workflowType,
        string? workflowId = null,
        LogLevel? logLevel = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20);
    Task<IEnumerable<Log>> GetCriticalLogsByWorkflowTypesAsync(
        string tenantId,
        IEnumerable<string> workflowTypes,
        DateTime? startTime = null,
        DateTime? endTime = null);
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
            filter &= Builders<Log>.Filter.Eq(x => x.Level, (LogLevel)logLevel.Value);
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

    public async Task<List<Log>> GetByLogLevelAsync(LogLevel level)
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

    public async Task<IEnumerable<Log>> GetLogsAsync(
        FilterDefinition<Log> filter,
        int skip = 0,
        int limit = 20,
        SortDefinition<Log>? sort = null)
    {
        var findOptions = new FindOptions<Log>
        {
            Skip = skip,
            Limit = limit,
            Sort = sort ?? Builders<Log>.Sort.Descending(l => l.CreatedAt)
        };

        return await _logs.Find(filter).Skip(skip).Limit(limit).Sort(sort).ToListAsync();
    }

    public async Task<long> CountAsync(FilterDefinition<Log> filter)
    {
        return await _logs.CountDocumentsAsync(filter);
    }
    
    public async Task<(IEnumerable<string?> participants, long totalCount)> GetDistinctParticipantsForAgentAsync(
        string tenantId, 
        string agent, 
        int page = 1, 
        int pageSize = 20)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, agent) & 
                    Builders<Log>.Filter.Ne(x => x.ParticipantId, null) &
                    Builders<Log>.Filter.Ne(x => x.ParticipantId, "");
        
        // Get distinct participant IDs using aggregate
        var distinctParticipants = await _logs.Distinct(x => x.ParticipantId, filter).ToListAsync();
        distinctParticipants = distinctParticipants.Where(x => x != null).ToList();
        distinctParticipants.Sort(); // Order by participant ID
        
        // Get total count
        var totalCount = distinctParticipants.Count;
        
        // Apply pagination
        var paginatedParticipants = distinctParticipants
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
            
        return (paginatedParticipants, totalCount);
    }
    
    public async Task<IEnumerable<string>> GetDistinctWorkflowTypesAsync(
        string tenantId, 
        string agent, 
        string? participantId = null)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, agent) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, null) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, "");
        
        if (!string.IsNullOrEmpty(participantId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }
        
        var workflowTypes = await _logs.Distinct(x => x.WorkflowType, filter).ToListAsync();
        workflowTypes.Sort(); // Order by workflow type
        
        return workflowTypes;
    }
    
    public async Task<IEnumerable<string>> GetDistinctWorkflowIdsForTypeAsync(
        string tenantId, 
        string agent, 
        string workflowType, 
        string? participantId = null)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, agent) &
                    Builders<Log>.Filter.Eq(x => x.WorkflowType, workflowType) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowId, null) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowId, "");
        
        if (!string.IsNullOrEmpty(participantId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }
        
        var workflowIds = await _logs.Distinct(x => x.WorkflowId, filter).ToListAsync();
        workflowIds.Sort(); // Order by workflow ID
        
        return workflowIds;
    }
    
    public async Task<(IEnumerable<Log> logs, long totalCount)> GetFilteredLogsAsync(
        string tenantId,
        string agent,
        string? participantId,
        string? workflowType,
        string? workflowId = null,
        LogLevel? logLevel = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, agent);
        
        if (!string.IsNullOrEmpty(participantId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }
        
        if (!string.IsNullOrEmpty(workflowType))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.WorkflowType, workflowType);
        }
        
        if (!string.IsNullOrEmpty(workflowId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.WorkflowId, workflowId);
        }
        
        if (logLevel.HasValue)
        {
            filter &= Builders<Log>.Filter.Eq(x => x.Level, logLevel.Value);
        }

        if (startTime.HasValue)
        {
            filter &= Builders<Log>.Filter.Gte(x => x.CreatedAt, startTime.Value);
        }

        if (endTime.HasValue)
        {
            filter &= Builders<Log>.Filter.Lte(x => x.CreatedAt, endTime.Value);
        }
        
        // Get total count
        var totalCount = await _logs.CountDocumentsAsync(filter);
        
        // Apply pagination and sorting
        var logs = await _logs.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
            
        return (logs, totalCount);
    }

    /// <summary>
    /// Gets the last log of each workflow run for specific workflow types, but only if it's a critical log
    /// </summary>
    public async Task<IEnumerable<Log>> GetCriticalLogsByWorkflowTypesAsync(
        string tenantId,
        IEnumerable<string> workflowTypes,
        DateTime? startTime = null,
        DateTime? endTime = null)
    {
        var matchStage = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                         Builders<Log>.Filter.In(x => x.WorkflowType, workflowTypes) &
                         Builders<Log>.Filter.Eq(x => x.Level, (LogLevel)5);
        
        if (startTime.HasValue)
        {
            matchStage &= Builders<Log>.Filter.Gte(x => x.CreatedAt, startTime.Value);
        }
        
        if (endTime.HasValue)
        {
            matchStage &= Builders<Log>.Filter.Lte(x => x.CreatedAt, endTime.Value);
        }
        
        // Get all critical logs that match our criteria
        var criticalLogs = await _logs.Aggregate()
            .Match(matchStage)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
        
        return criticalLogs;
    }
}
