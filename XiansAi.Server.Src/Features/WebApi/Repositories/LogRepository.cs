using Features.WebApi.Models;
using MongoDB.Driver;
using Shared.Data;

namespace Features.WebApi.Repositories;

public interface ILogRepository
{
    Task<List<Log>> GetByWorkflowRunIdAsync(string workflowRunId, int skip, int limit, int? logLevel = null);
    Task<List<Log>> GetByLogLevelAsync(LogLevel level);
    Task<List<Log>> GetLastLogAsync(DateTime? startTime, DateTime? endTime);
    Task<List<Log>> GetLastLogsByRunIdsAsync(IEnumerable<string> runIds);
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
    Task<(IEnumerable<Log> logs, long totalCount)> GetAdminLogsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string[]? workflowIds,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page = 1,
        int pageSize = 20);

    Task<(IEnumerable<LogStreamSummary> streams, long totalCount)> GetAdminLogStreamsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page = 1,
        int pageSize = 20);
}

/// <summary>
/// Aggregated summary of logs for a single workflow ("stream"), used by admin log-stream queries.
/// </summary>
public class LogStreamSummary
{
    public required string WorkflowId { get; set; }
    public string? WorkflowType { get; set; }
    public string? WorkflowRunId { get; set; }
    public required string Agent { get; set; }
    public string? Activation { get; set; }
    public string? ParticipantId { get; set; }
    public required DateTime LastLogAt { get; set; }
    public required DateTime FirstLogAt { get; set; }
    public required long LogCount { get; set; }
    public required LogLevel LastLogLevel { get; set; }
    public required string LastLogMessage { get; set; }
}

public class LogRepository : ILogRepository
{
    private const string CollectionName = "logs";
    private readonly IMongoCollection<Log> _logs;

    public LogRepository(IMongoDbClientService mongoDbClientService)
    {
        ArgumentNullException.ThrowIfNull(mongoDbClientService);
        _logs = mongoDbClientService.GetCollection<Log>(CollectionName);
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

    /// <summary>
    /// Gets the most recent log per workflow run, scoped to the given run IDs.
    /// Efficient for paginated workflow lists—avoids full-collection scan.
    /// </summary>
    public async Task<List<Log>> GetLastLogsByRunIdsAsync(IEnumerable<string> runIds)
    {
        var runIdList = runIds.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        if (runIdList.Count == 0)
        {
            return new List<Log>();
        }

        var filter = Builders<Log>.Filter.In(x => x.WorkflowRunId, runIdList);
        return await _logs.Aggregate()
            .Match(filter)
            .SortByDescending(x => x.CreatedAt)
            .Group(x => x.WorkflowRunId, g => g.First())
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
        var sortDefinition = sort ?? Builders<Log>.Sort.Descending(l => l.CreatedAt);
        return await _logs.Find(filter).Skip(skip).Limit(limit).Sort(sortDefinition).ToListAsync();
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
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId) &
                    Builders<Log>.Filter.Eq(x => x.Agent, agent) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, null) &
                    Builders<Log>.Filter.Ne(x => x.WorkflowType, "");
        if (!string.IsNullOrEmpty(participantId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }
        var workflowTypes = await _logs.Distinct(x => x.WorkflowType, filter).ToListAsync();
        var nonNullWorkflowTypes = workflowTypes.Where(x => x != null).Select(x => x!).ToList();
        nonNullWorkflowTypes.Sort();
        return nonNullWorkflowTypes;
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

    /// <summary>
    /// Gets logs with flexible filtering for admin purposes.
    /// Unlike GetFilteredLogsAsync, this method allows optional agent filtering for cross-agent queries.
    /// Supports filtering by one or more workflow IDs (streams).
    /// </summary>
    public async Task<(IEnumerable<Log> logs, long totalCount)> GetAdminLogsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string[]? workflowIds,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page = 1,
        int pageSize = 20)
    {
        var filter = BuildAdminLogsFilter(
            tenantId,
            agentName,
            activationName,
            participantId,
            workflowIds,
            workflowType,
            logLevels,
            startDate,
            endDate);

        var totalCount = await _logs.CountDocumentsAsync(filter);
        var logs = await _logs.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (logs, totalCount);
    }

    /// <summary>
    /// Gets a paginated list of distinct log streams (grouped by workflow_id) for a tenant,
    /// sorted by the most recent log timestamp (descending). Each stream summarizes its latest log,
    /// total log count and time range. Intended as the first step of a two-step admin log query:
    /// list streams, then fetch logs filtered by selected workflow IDs.
    /// </summary>
    public async Task<(IEnumerable<LogStreamSummary> streams, long totalCount)> GetAdminLogStreamsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page = 1,
        int pageSize = 20)
    {
        var filter = BuildAdminLogsFilter(
            tenantId,
            agentName,
            activationName,
            participantId,
            workflowIds: null,
            workflowType,
            logLevels,
            startDate,
            endDate);

        // Exclude logs without a workflow_id - they cannot form a stream.
        filter &= Builders<Log>.Filter.Ne(x => x.WorkflowId, null) &
                  Builders<Log>.Filter.Ne(x => x.WorkflowId, string.Empty);

        // Group by workflow_id, capture summary fields from the most recent log in the group.
        var pipeline = _logs.Aggregate()
            .Match(filter)
            .SortByDescending(x => x.CreatedAt)
            .Group(x => x.WorkflowId, g => new LogStreamSummary
            {
                WorkflowId = g.Key,
                WorkflowType = g.First().WorkflowType,
                WorkflowRunId = g.First().WorkflowRunId,
                Agent = g.First().Agent,
                Activation = g.First().Activation,
                ParticipantId = g.First().ParticipantId,
                LastLogAt = g.First().CreatedAt,
                FirstLogAt = g.Last().CreatedAt,
                LogCount = g.LongCount(),
                LastLogLevel = g.First().Level,
                LastLogMessage = g.First().Message
            });

        // Count distinct streams for pagination metadata before applying skip/limit.
        var countResult = await pipeline.Count().FirstOrDefaultAsync();
        var totalCount = countResult?.Count ?? 0L;

        var streams = await pipeline
            .SortByDescending(s => s.LastLogAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (streams, totalCount);
    }

    /// <summary>
    /// Builds the shared MongoDB filter used by admin log queries.
    /// </summary>
    private static FilterDefinition<Log> BuildAdminLogsFilter(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string[]? workflowIds,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate)
    {
        var filter = Builders<Log>.Filter.Eq(x => x.TenantId, tenantId);

        if (!string.IsNullOrEmpty(agentName))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.Agent, agentName);
        }

        if (!string.IsNullOrEmpty(activationName))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.Activation, activationName);
        }

        if (!string.IsNullOrEmpty(participantId))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.ParticipantId, participantId);
        }

        if (workflowIds != null && workflowIds.Length > 0)
        {
            filter &= workflowIds.Length == 1
                ? Builders<Log>.Filter.Eq(x => x.WorkflowId, workflowIds[0])
                : Builders<Log>.Filter.In(x => x.WorkflowId, workflowIds);
        }

        if (!string.IsNullOrEmpty(workflowType))
        {
            filter &= Builders<Log>.Filter.Eq(x => x.WorkflowType, workflowType);
        }

        if (logLevels != null && logLevels.Length > 0)
        {
            filter &= Builders<Log>.Filter.In(x => x.Level, logLevels);
        }

        if (startDate.HasValue)
        {
            filter &= Builders<Log>.Filter.Gte(x => x.CreatedAt, startDate.Value);
        }

        if (endDate.HasValue)
        {
            filter &= Builders<Log>.Filter.Lte(x => x.CreatedAt, endDate.Value);
        }

        return filter;
    }
}
