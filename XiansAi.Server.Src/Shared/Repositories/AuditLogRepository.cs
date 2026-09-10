using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories;

public interface IAuditLogRepository
{
    Task CreateAsync(AuditLogEntry entry);

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
    private readonly IMongoCollection<AuditLogEntry> _auditLogs;

    public AuditLogRepository(IMongoDbClientService mongoDbClientService)
    {
        ArgumentNullException.ThrowIfNull(mongoDbClientService);
        _auditLogs = mongoDbClientService.GetCollection<AuditLogEntry>(CollectionName);
    }

    public async Task CreateAsync(AuditLogEntry entry)
    {
        await _auditLogs.InsertOneAsync(entry);
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

    public async Task<IEnumerable<string>> GetDistinctPerformedByAsync(string tenantId)
    {
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.TenantId, tenantId);
        var values = await _auditLogs.Distinct(x => x.ParticipantId, filter).ToListAsync();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<string>> GetDistinctActivationNamesAsync(string tenantId)
    {
        var filter = Builders<AuditLogEntry>.Filter.Eq(x => x.TenantId, tenantId);
        var values = await _auditLogs.Distinct(x => x.ActivationName, filter).ToListAsync();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)!;
    }
}
