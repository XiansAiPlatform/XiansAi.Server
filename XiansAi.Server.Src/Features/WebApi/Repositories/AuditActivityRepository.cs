using Features.WebApi.Models;
using MongoDB.Driver;
using Shared.Data;

namespace Features.WebApi.Repositories;

public interface IAuditActivityRepository
{
    Task CreateAsync(AuditActivity activity);

    Task<(IEnumerable<AuditActivity> activities, long totalCount)> GetFilteredAsync(
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

public class AuditActivityRepository : IAuditActivityRepository
{
    private const string CollectionName = "audit_activities";
    private readonly IMongoCollection<AuditActivity> _auditActivities;

    public AuditActivityRepository(IMongoDbClientService mongoDbClientService)
    {
        ArgumentNullException.ThrowIfNull(mongoDbClientService);
        _auditActivities = mongoDbClientService.GetCollection<AuditActivity>(CollectionName);
    }

    public async Task CreateAsync(AuditActivity activity)
    {
        await _auditActivities.InsertOneAsync(activity);
    }

    public async Task<(IEnumerable<AuditActivity> activities, long totalCount)> GetFilteredAsync(
        string tenantId,
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20)
    {
        var filter = Builders<AuditActivity>.Filter.Eq(x => x.TenantId, tenantId);

        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            filter &= Builders<AuditActivity>.Filter.Eq(x => x.PerformedBy, performedBy);
        }

        if (onlyWithoutActivation)
        {
            filter &= Builders<AuditActivity>.Filter.Or(
                Builders<AuditActivity>.Filter.Eq(x => x.ActivationName, null),
                Builders<AuditActivity>.Filter.Eq(x => x.ActivationName, string.Empty));
        }
        else if (!string.IsNullOrWhiteSpace(activationName))
        {
            filter &= Builders<AuditActivity>.Filter.Eq(x => x.ActivationName, activationName);
        }

        if (startTime.HasValue)
        {
            filter &= Builders<AuditActivity>.Filter.Gte(x => x.CreatedAt, startTime.Value);
        }

        if (endTime.HasValue)
        {
            filter &= Builders<AuditActivity>.Filter.Lte(x => x.CreatedAt, endTime.Value);
        }

        var totalCount = await _auditActivities.CountDocumentsAsync(filter);

        var activities = await _auditActivities.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (activities, totalCount);
    }

    public async Task<IEnumerable<string>> GetDistinctPerformedByAsync(string tenantId)
    {
        var filter = Builders<AuditActivity>.Filter.Eq(x => x.TenantId, tenantId);
        var values = await _auditActivities.Distinct(x => x.PerformedBy, filter).ToListAsync();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<string>> GetDistinctActivationNamesAsync(string tenantId)
    {
        var filter = Builders<AuditActivity>.Filter.Eq(x => x.TenantId, tenantId);
        var values = await _auditActivities.Distinct(x => x.ActivationName, filter).ToListAsync();
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)!;
    }
}
