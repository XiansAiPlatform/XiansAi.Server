using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services;

namespace Shared.Repositories;

public interface IAuditLogRepository
{
    Task CreateAsync(AuditLogEntry entry);

    /// <summary>
    /// Latest row matching tenant, action, actor, and <c>details.targetParticipantId</c>
    /// created at or after <paramref name="createdAtOrAfter"/>. Used to collapse repeated
    /// view-as reads into one logical session.
    /// </summary>
    Task<AuditLogEntry?> FindRecentMatchingAsync(
        string tenantId,
        string action,
        string participantId,
        string targetParticipantId,
        DateTime createdAtOrAfter);

    Task ReplaceAsync(AuditLogEntry entry);

    Task<(IEnumerable<AuditLogEntry> entries, long totalCount)> GetFilteredAsync(
        string tenantId,
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20);

    /// <summary>Distinct, non-empty "performed by" values recorded for the tenant, sorted alphabetically.</summary>
    Task<IEnumerable<string>> GetDistinctPerformedByAsync(string tenantId);

    /// <summary>Distinct, non-empty activation names recorded for the tenant, sorted alphabetically.</summary>
    Task<IEnumerable<string>> GetDistinctActivationNamesAsync(string tenantId);
}

public class AuditLogRepository : IAuditLogRepository
{
    private const string CollectionName = "audit_logs";
    private const string PerformedByCacheKeyPrefix = "auditlog:performed-by:";
    private const string ActivationNamesCacheKeyPrefix = "auditlog:activation-names:";
    private const int MaxDistinctOptions = 200;
    private static readonly TimeSpan DistinctOptionsCacheDuration = TimeSpan.FromMinutes(2);

    private readonly IMongoCollection<AuditLogEntry> _auditLogs;
    private readonly IAsyncResultCache _cache;

    public AuditLogRepository(IMongoDbClientService mongoDbClientService, IAsyncResultCache cache)
    {
        ArgumentNullException.ThrowIfNull(mongoDbClientService);
        _auditLogs = mongoDbClientService.GetCollection<AuditLogEntry>(CollectionName);
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task CreateAsync(AuditLogEntry entry)
    {
        await _auditLogs.InsertOneAsync(entry);
        InvalidateDistinctCaches(entry.TenantId);
    }

    public async Task<AuditLogEntry?> FindRecentMatchingAsync(
        string tenantId,
        string action,
        string participantId,
        string targetParticipantId,
        DateTime createdAtOrAfter)
    {
        var filter = Builders<AuditLogEntry>.Filter.And(
            Builders<AuditLogEntry>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<AuditLogEntry>.Filter.Eq(x => x.Action, action),
            Builders<AuditLogEntry>.Filter.Eq(x => x.ParticipantId, participantId),
            Builders<AuditLogEntry>.Filter.Eq("details.targetParticipantId", targetParticipantId),
            Builders<AuditLogEntry>.Filter.Gte(x => x.CreatedAt, createdAtOrAfter));

        return await _auditLogs.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task ReplaceAsync(AuditLogEntry entry)
    {
        await _auditLogs.ReplaceOneAsync(x => x.Id == entry.Id, entry);
        InvalidateDistinctCaches(entry.TenantId);
    }

    public async Task<(IEnumerable<AuditLogEntry> entries, long totalCount)> GetFilteredAsync(
        string tenantId,
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20)
    {
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.TenantId, tenantId);

        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            filter &= Builders<AuditLogEntry>.Filter.Eq(x => x.ParticipantId, performedBy);
        }

        if (onlyWithoutActivation)
        {
            filter &= Builders<AuditLogEntry>.Filter.Or(
                Builders<AuditLogEntry>.Filter.Eq(x => x.ActivationName, null),
                Builders<AuditLogEntry>.Filter.Eq(x => x.ActivationName, string.Empty));
        }
        else if (!string.IsNullOrWhiteSpace(activationName))
        {
            filter &= Builders<AuditLogEntry>.Filter.Eq(x => x.ActivationName, activationName);
        }

        if (startTime.HasValue)
        {
            filter &= Builders<AuditLogEntry>.Filter.Gte(x => x.CreatedAt, startTime.Value);
        }

        if (endTime.HasValue)
        {
            filter &= Builders<AuditLogEntry>.Filter.Lte(x => x.CreatedAt, endTime.Value);
        }

        var totalCount = await _auditLogs.CountDocumentsAsync(filter);

        var entries = await _auditLogs.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (entries, totalCount);
    }

    public Task<IEnumerable<string>> GetDistinctPerformedByAsync(string tenantId) =>
        GetCachedDistinctAsync(PerformedByCacheKeyPrefix, tenantId, "participant_id");

    public Task<IEnumerable<string>> GetDistinctActivationNamesAsync(string tenantId) =>
        GetCachedDistinctAsync(ActivationNamesCacheKeyPrefix, tenantId, "activation_name");

    private async Task<IEnumerable<string>> GetCachedDistinctAsync(
        string cacheKeyPrefix, string tenantId, string fieldName)
    {
        var values = await _cache.GetOrAddAsync(
            cacheKeyPrefix + tenantId,
            _ => LoadDistinctValuesAsync(tenantId, fieldName),
            DistinctOptionsCacheDuration);

        return values;
    }

    /// <summary>
    /// Distinct values for a dropdown: empty values are excluded in the query, results are sorted
    /// and capped so a tenant with a large audit history cannot return an unbounded scan.
    /// </summary>
    private async Task<List<string>> LoadDistinctValuesAsync(string tenantId, string fieldName)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "tenant_id", tenantId },
                { fieldName, new BsonDocument("$nin", new BsonArray { BsonNull.Value, string.Empty }) }
            }),
            new BsonDocument("$group", new BsonDocument("_id", "$" + fieldName)),
            new BsonDocument("$sort", new BsonDocument("_id", 1)),
            new BsonDocument("$limit", MaxDistinctOptions)
        };

        var results = await _auditLogs.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return results
            .Select(AsNonEmptyId)
            .Where(value => value != null)
            .Cast<string>()
            .ToList();
    }

    private static string? AsNonEmptyId(BsonDocument document)
    {
        var value = document.GetValue("_id", BsonNull.Value);
        if (!value.IsString)
        {
            return null;
        }

        var text = value.AsString;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private void InvalidateDistinctCaches(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        _cache.Remove(PerformedByCacheKeyPrefix + tenantId);
        _cache.Remove(ActivationNamesCacheKeyPrefix + tenantId);
    }
}
