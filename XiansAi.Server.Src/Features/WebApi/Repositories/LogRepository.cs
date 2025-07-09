using Features.WebApi.Models;
using MongoDB.Driver;
using Shared.Data;
using Shared.Utils;

namespace Features.WebApi.Repositories;

public interface ILogRepository
{
    Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null);
    Task<List<Log>> GetByLogLevelAsync(LogLevel level);
    Task<List<Log>> GetLastLogAsync(DateTime? startTime, DateTime? endTime);
    Task<List<Log>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    
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
        var database = databaseService.GetDatabaseAsync().Result;
        _logs = database.GetCollection<Log>("logs");
    }

    public async Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.WorkflowRunId, workflowRunId);
        if (logLevel.HasValue)
        {
            filter &= Builders<Log>.Filter.Eq(x => x.Level, (LogLevel)logLevel.Value);
        }

        var totalCount = (int)await _logs.CountDocumentsAsync(filter);
        if (skip >= totalCount)
        {
            return new List<Log>();
        }
        if (skip + limit > totalCount)
        {
            limit = Math.Max(0, totalCount - skip);
        }
        var adjustedSkip = Math.Max(0, totalCount - limit - skip);
        var adjustedLimit = Math.Min(limit, totalCount - adjustedSkip);
        var logs = await _logs.Find(filter)
            .Skip(adjustedSkip)
            .Limit(adjustedLimit)
            .ToListAsync();
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
        // Sanitize and validate inputs
        var sanitizedId = InputSanitizationUtils.SanitizeObjectId(id);
        InputValidationUtils.ValidateObjectId(sanitizedId, nameof(id));

        var sanitizedLog = InputSanitizationUtils.SanitizeLog(log);
        InputValidationUtils.ValidateLog(sanitizedLog);

        if (sanitizedLog == null)
        {
            throw new ArgumentException("Log cannot be null after sanitization");
        }

        var result = await _logs.ReplaceOneAsync(x => x.Id == sanitizedId, sanitizedLog);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdatePropertiesAsync(string id, Dictionary<string, object> properties)
    {
        // Sanitize and validate inputs ---ok--
        var sanitizedId = InputSanitizationUtils.SanitizeObjectId(id);
        InputValidationUtils.ValidateObjectId(sanitizedId, nameof(id));

        if (properties == null)
        {
            throw new ArgumentException("Properties cannot be null", nameof(properties));
        }

        // Sanitize properties dictionary
        var sanitizedProperties = new Dictionary<string, object>();
        foreach (var kvp in properties)
        {
            var sanitizedKey = InputSanitizationUtils.SanitizeStringStrict(kvp.Key, 100);
            if (!string.IsNullOrWhiteSpace(sanitizedKey))
            {
                // For string values, sanitize them
                if (kvp.Value is string stringValue)
                {
                    sanitizedProperties[sanitizedKey] = InputSanitizationUtils.SanitizeString(stringValue);
                }
                else
                {
                    sanitizedProperties[sanitizedKey] = kvp.Value;
                }
            }
        }

        var update = Builders<Log>.Update.Set(x => x.Properties, sanitizedProperties);
        var result = await _logs.UpdateOneAsync(x => x.Id == sanitizedId, update);
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
        // Sanitize and validate inputs --ok--
        var sanitizedTenantId = InputSanitizationUtils.SanitizeTenantId(tenantId);
        InputValidationUtils.ValidateTenantId(sanitizedTenantId, nameof(tenantId));

        var sanitizedAgent = InputSanitizationUtils.SanitizeAgent(agent);
        InputValidationUtils.ValidateAgentName(sanitizedAgent, nameof(agent));

        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, sanitizedTenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, sanitizedAgent) & 
                    Builders<Log>.Filter.Ne(x => x.ParticipantId, null) &
                    Builders<Log>.Filter.Ne(x => x.ParticipantId, "");
        var distinctParticipants = await _logs.Distinct(x => x.ParticipantId, filter).ToListAsync();
        distinctParticipants = distinctParticipants.Where(x => x != null).ToList();
        distinctParticipants.Sort();
        var totalCount = distinctParticipants.Count;
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
        // Sanitize and validate inputs
        InputValidationUtils.ValidateTenantId(tenantId, nameof(tenantId));

        var sanitizedAgent = InputSanitizationUtils.SanitizeAgent(agent);
        InputValidationUtils.ValidateAgentName(sanitizedAgent, nameof(agent));

        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, sanitizedAgent) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, null) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, "");
        
        if (!string.IsNullOrEmpty(participantId))
        {
            InputValidationUtils.ValidateParticipantId(participantId, nameof(participantId));
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }
        
        var workflowTypes = await _logs.Distinct(x => x.WorkflowType, filter).ToListAsync();
        workflowTypes.Sort();
        return workflowTypes;
    }
    
    public async Task<IEnumerable<string>> GetDistinctWorkflowIdsForTypeAsync(
        string tenantId, 
        string agent, 
        string workflowType, 
        string? participantId = null)
    {
        // Sanitize and validate inputs
        InputValidationUtils.ValidateTenantId(tenantId, nameof(tenantId));

        var sanitizedAgent = InputSanitizationUtils.SanitizeAgent(agent);
        InputValidationUtils.ValidateAgentName(sanitizedAgent, nameof(agent));

        var sanitizedWorkflowType = InputSanitizationUtils.SanitizeWorkflowType(workflowType);
        InputValidationUtils.ValidateWorkflowType(sanitizedWorkflowType, nameof(workflowType));

        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, sanitizedAgent) &
                    Builders<Log>.Filter.Eq(x => x.WorkflowType, sanitizedWorkflowType) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowId, null) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowId, "");
        if (!string.IsNullOrEmpty(participantId))
        {
            var sanitizedParticipantId = InputSanitizationUtils.SanitizeParticipantId(participantId);
            InputValidationUtils.ValidateParticipantId(sanitizedParticipantId, nameof(participantId));
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, sanitizedParticipantId);
        }
        var workflowIds = await _logs.Distinct(x => x.WorkflowId, filter).ToListAsync();
        workflowIds.Sort();
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
        var totalCount = await _logs.CountDocumentsAsync(filter);
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
        var criticalLogs = await _logs.Aggregate()
            .Match(matchStage)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
        return criticalLogs;
    }
}
