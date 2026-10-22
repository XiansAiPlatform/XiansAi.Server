using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models.HealthChecks;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>A page of runs and the total number of matching runs.</summary>
public sealed record HealthCheckRunPage(List<AgentHealthCheckRun> Items, long TotalCount, int Page, int PageSize);

/// <summary>
/// Saves and reads health checks (<c>agent_health_checks</c>) and their runs (<c>agent_health_check_runs</c>).
/// All queries filter by tenant, except the reconciler's list of all checks.
/// </summary>
public interface IAgentHealthCheckRepository
{
    /// <summary>Adds a check. Returns false if the activation already has a check with that name.</summary>
    Task<bool> InsertAsync(AgentHealthCheck check);

    Task<AgentHealthCheck?> GetByIdAsync(string tenantId, string checkId);
    Task<List<AgentHealthCheck>> ListByActivationAsync(string tenantId, string activationId);
    Task<long> CountByActivationAsync(string tenantId, string activationId);
    Task<List<AgentHealthCheck>> ListByTenantAsync(string tenantId, IReadOnlyCollection<HealthCheckStatus>? statuses, string? agentName);

    /// <summary>All checks in all tenants, for the reconciler.</summary>
    Task<List<AgentHealthCheck>> ListAllAsync();

    /// <summary>Replaces the check. Returns null if it was deleted, or false if the name is already used.</summary>
    Task<bool?> ReplaceAsync(AgentHealthCheck check);

    Task<bool> DeleteAsync(string tenantId, string checkId);
    Task SetScheduleStateAsync(string checkId, HealthCheckScheduleState state);

    /// <summary>Saves the latest status after a run. Does nothing if the check was deleted.</summary>
    Task RecordRunResultAsync(string checkId, HealthCheckStatus status, DateTime runAt, string? detail);

    Task InsertRunAsync(AgentHealthCheckRun run);
    Task<HealthCheckRunPage> ListRunsAsync(string tenantId, string checkId, DateTime? from, DateTime? to, HealthCheckStatus? status, int page, int pageSize);
    Task DeleteRunsByCheckAsync(string tenantId, string checkId);
}

public class AgentHealthCheckRepository : IAgentHealthCheckRepository
{
    public const string ChecksCollectionName = "agent_health_checks";
    public const string RunsCollectionName = "agent_health_check_runs";

    private readonly IMongoCollection<AgentHealthCheck> _checks;
    private readonly IMongoCollection<AgentHealthCheckRun> _runs;
    private readonly ILogger<AgentHealthCheckRepository> _logger;

    public AgentHealthCheckRepository(IDatabaseService databaseService, ILogger<AgentHealthCheckRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _checks = database.GetCollection<AgentHealthCheck>(ChecksCollectionName);
        _runs = database.GetCollection<AgentHealthCheckRun>(RunsCollectionName);
    }

    public async Task<bool> InsertAsync(AgentHealthCheck check)
    {
        // No retry helper here: a duplicate name is a normal user error and shouldn't be logged as an error.
        try
        {
            await _checks.InsertOneAsync(check);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<AgentHealthCheck?> GetByIdAsync(string tenantId, string checkId)
    {
        if (!ObjectId.TryParse(checkId, out _))
        {
            return null;
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks.Find(c => c.TenantId == tenantId && c.Id == checkId).FirstOrDefaultAsync(),
            _logger, operationName: "GetHealthCheck");
    }

    public async Task<List<AgentHealthCheck>> ListByActivationAsync(string tenantId, string activationId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks
                .Find(c => c.TenantId == tenantId && c.ActivationId == activationId)
                .SortBy(c => c.Name)
                .ToListAsync(),
            _logger, operationName: "ListHealthChecksByActivation");
    }

    public async Task<long> CountByActivationAsync(string tenantId, string activationId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks.CountDocumentsAsync(c => c.TenantId == tenantId && c.ActivationId == activationId),
            _logger, operationName: "CountHealthChecksByActivation");
    }

    public async Task<List<AgentHealthCheck>> ListByTenantAsync(
        string tenantId, IReadOnlyCollection<HealthCheckStatus>? statuses, string? agentName)
    {
        var builder = Builders<AgentHealthCheck>.Filter;
        var filter = builder.Eq(c => c.TenantId, tenantId);

        if (statuses is { Count: > 0 })
        {
            filter &= builder.In(c => c.LastStatus, statuses.Select(s => (HealthCheckStatus?)s));
        }

        if (!string.IsNullOrWhiteSpace(agentName))
        {
            filter &= builder.Eq(c => c.AgentName, agentName);
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks
                .Find(filter)
                .SortBy(c => c.AgentName).ThenBy(c => c.ActivationName).ThenBy(c => c.Name)
                .ToListAsync(),
            _logger, operationName: "ListHealthChecksByTenant");
    }

    public async Task<List<AgentHealthCheck>> ListAllAsync()
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks.Find(FilterDefinition<AgentHealthCheck>.Empty).ToListAsync(),
            _logger, operationName: "ListAllHealthChecks");
    }

    public async Task<bool?> ReplaceAsync(AgentHealthCheck check)
    {
        try
        {
            var result = await _checks.ReplaceOneAsync(c => c.TenantId == check.TenantId && c.Id == check.Id, check);
            return result.MatchedCount > 0 ? true : null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string tenantId, string checkId)
    {
        if (!ObjectId.TryParse(checkId, out _))
        {
            return false;
        }

        var result = await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _checks.DeleteOneAsync(c => c.TenantId == tenantId && c.Id == checkId),
            _logger, operationName: "DeleteHealthCheck");
        return result.DeletedCount > 0;
    }

    public async Task SetScheduleStateAsync(string checkId, HealthCheckScheduleState state)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _checks.UpdateOneAsync(
                c => c.Id == checkId,
                Builders<AgentHealthCheck>.Update.Set(c => c.ScheduleState, state));
        }, _logger, operationName: "SetHealthCheckScheduleState");
    }

    public async Task RecordRunResultAsync(string checkId, HealthCheckStatus status, DateTime runAt, string? detail)
    {
        var update = Builders<AgentHealthCheck>.Update
            .Set(c => c.LastRunAt, runAt)
            .Set(c => c.LastStatus, status)
            .Set(c => c.LastDetail, detail);

        update = status == HealthCheckStatus.Pass
            ? update.Set(c => c.ConsecutiveFailures, 0).Set(c => c.LastSuccessAt, runAt)
            : update.Inc(c => c.ConsecutiveFailures, 1);

        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _checks.UpdateOneAsync(c => c.Id == checkId, update);
        }, _logger, operationName: "RecordHealthCheckRunResult");
    }

    public async Task InsertRunAsync(AgentHealthCheckRun run)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _runs.InsertOneAsync(run);
        }, _logger, operationName: "InsertHealthCheckRun");
    }

    public async Task<HealthCheckRunPage> ListRunsAsync(
        string tenantId, string checkId, DateTime? from, DateTime? to, HealthCheckStatus? status, int page, int pageSize)
    {
        var builder = Builders<AgentHealthCheckRun>.Filter;
        var filter = builder.Eq(r => r.TenantId, tenantId) & builder.Eq(r => r.CheckId, checkId);

        if (from.HasValue) filter &= builder.Gte(r => r.StartedAt, from.Value);
        if (to.HasValue) filter &= builder.Lte(r => r.StartedAt, to.Value);
        if (status.HasValue) filter &= builder.Eq(r => r.Status, status.Value);

        var total = await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _runs.CountDocumentsAsync(filter),
            _logger, operationName: "CountHealthCheckRuns");

        var items = await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _runs
                .Find(filter)
                .SortByDescending(r => r.StartedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(),
            _logger, operationName: "ListHealthCheckRuns");

        return new HealthCheckRunPage(items, total, page, pageSize);
    }

    public async Task DeleteRunsByCheckAsync(string tenantId, string checkId)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _runs.DeleteManyAsync(r => r.TenantId == tenantId && r.CheckId == checkId);
        }, _logger, operationName: "DeleteHealthCheckRuns");
    }
}
