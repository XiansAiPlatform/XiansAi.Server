using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITenantRepository
{
    Task<Tenant> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByTenantIdCaseInsensitiveAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<Tenant> GetByDomainAsync(string domain, CancellationToken cancellationToken = default);
    Task<List<Tenant>> GetByDomainListAsync(string domain, CancellationToken cancellationToken = default);
    Task<List<Tenant>> GetByTenantIdsAsync(IEnumerable<string> tenantIds, CancellationToken cancellationToken = default);
    Task<List<Tenant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Tenant>> GetPagedAsync(int skip, int limit, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<long> CountAsync(string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<List<string>> GetAllTenantIdsAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(Tenant tenant);
    Task<bool> UpdateAsync(string id, Tenant tenant);
    Task<bool> DeleteAsync(string id);
    Task<List<Tenant>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<List<Tenant>> GetTenantsByCreatorAsync(string createdBy);
    Task<bool> AddAgentAsync(string tenantId, Agent agent);
    Task<bool> UpdateAgentAsync(string tenantId, string agentName, Agent updatedAgent);
    Task<bool> RemoveAgentAsync(string tenantId, string agentName);
    Task<bool> AddFlowToAgentAsync(string tenantId, string agentName, Flow flow);
}

public class TenantRepository : ITenantRepository
{
    private const string CollectionName = "tenants";

    private readonly IMongoCollection<Tenant> _collection;
    private readonly ILogger<TenantRepository> _logger;

    public TenantRepository(IMongoDbClientService mongoDbClientService, ILogger<TenantRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(mongoDbClientService);
        ArgumentNullException.ThrowIfNull(logger);
        _collection = mongoDbClientService.GetCollection<Tenant>(CollectionName);
        _logger = logger;
    }

    // Standard CRUD Operations
    public async Task<Tenant> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.Id == id).FirstOrDefaultAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantById");
    }

    public async Task<Tenant?> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.TenantId == tenantId).FirstOrDefaultAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByTenantId");
    }

    /// <summary>
    /// Finds a tenant whose tenant_id matches the given value ignoring case
    /// (e.g. "MyTenant" matches an existing "mytenant"). Used to enforce
    /// case-insensitive uniqueness of tenant ids at creation time.
    ///
    /// Matched with an anchored case-insensitive regex instead of a collation because
    /// Azure Cosmos DB's MongoDB API rejects collation on find ("collation is not
    /// supported in the find command yet"). The exact match is tried first so the common
    /// case still uses the unique tenant_id index.
    /// </summary>
    public async Task<Tenant?> GetByTenantIdCaseInsensitiveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required", nameof(tenantId));
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var exactMatch = await _collection
                .Find(tenant => tenant.TenantId == tenantId)
                .FirstOrDefaultAsync(cancellationToken);
            if (exactMatch != null)
            {
                return exactMatch;
            }

            return await _collection
                .Find(BuildCaseInsensitiveTenantIdFilter(tenantId))
                .FirstOrDefaultAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByTenantIdCaseInsensitive");
    }

    /// <summary>
    /// Builds a filter that matches tenant_id in full (not as a substring) ignoring case.
    /// The value is regex-escaped so characters such as '.' in a tenant id are matched literally.
    /// </summary>
    private static FilterDefinition<Tenant> BuildCaseInsensitiveTenantIdFilter(string tenantId)
    {
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(tenantId) + "$";
        return Builders<Tenant>.Filter.Regex(t => t.TenantId, new BsonRegularExpression(pattern, "i"));
    }

    public async Task<Tenant> GetByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.Domain == domain).FirstOrDefaultAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantByDomain");
    }

    public async Task<List<Tenant>> GetByDomainListAsync(string domain, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.And(
                Builders<Tenant>.Filter.Ne(t => t.Domain, null),
                Builders<Tenant>.Filter.Ne(t => t.Domain, string.Empty),
                Builders<Tenant>.Filter.Eq(t => t.Domain, domain)
            );
            return await _collection.Find(filter).ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByDomain");
    }

    public async Task<List<Tenant>> GetByTenantIdsAsync(IEnumerable<string> tenantIds, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.In(t => t.TenantId, tenantIds);
            return await _collection.Find(filter).ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByTenantIds");
    }

    public async Task<List<Tenant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(_ => true).ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllTenants");
    }

    public async Task<List<Tenant>> GetPagedAsync(int skip, int limit, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Sort by name for a stable, deterministic order across pages.
            return await _collection.Find(BuildSearchFilter(searchTerm))
                .SortBy(t => t.Name)
                .Skip(skip)
                .Limit(limit)
                .ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetPagedTenants");
    }

    public async Task<long> CountAsync(string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.CountDocumentsAsync(BuildSearchFilter(searchTerm), cancellationToken: cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CountTenants");
    }

    /// <summary>
    /// Builds the filter used by the paginated list: everything when no search term is
    /// given, otherwise a case-insensitive match on tenantId, name, domain or description.
    /// Shared by GetPagedAsync and CountAsync so page items and totals always agree.
    /// </summary>
    private static FilterDefinition<Tenant> BuildSearchFilter(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return Builders<Tenant>.Filter.Empty;
        }

        var escapedTerm = System.Text.RegularExpressions.Regex.Escape(searchTerm.Trim());
        var regex = new BsonRegularExpression(escapedTerm, "i");
        return Builders<Tenant>.Filter.Or(
            Builders<Tenant>.Filter.Regex(x => x.TenantId, regex),
            Builders<Tenant>.Filter.Regex(x => x.Name, regex),
            Builders<Tenant>.Filter.Regex(x => x.Domain, regex),
            Builders<Tenant>.Filter.Regex(x => x.Description, regex)
        );
    }

    public async Task<List<string>> GetAllTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var projection = Builders<Tenant>.Projection.Expression(t => t.TenantId);
            return await _collection.Find(_ => true).Project(projection).ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllTenantIds");
    }

    public async Task CreateAsync(Tenant tenant)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.InsertOneAsync(tenant);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateTenant");
    }

    public async Task<bool> UpdateAsync(string id, Tenant tenant)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            tenant.UpdatedAt = DateTime.UtcNow;
            var result = await _collection.ReplaceOneAsync(t => t.Id == id, tenant);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateTenant");
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _collection.DeleteOneAsync(tenant => tenant.Id == id);
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteTenant");
    }

    // Advanced Query Methods
    public async Task<List<Tenant>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var escapedTerm = System.Text.RegularExpressions.Regex.Escape(searchTerm);
            var filter = Builders<Tenant>.Filter.Or(
                Builders<Tenant>.Filter.Regex(x => x.Name, new BsonRegularExpression(escapedTerm, "i")),
                Builders<Tenant>.Filter.Regex(x => x.Domain, new BsonRegularExpression(escapedTerm, "i")),
                Builders<Tenant>.Filter.Regex(x => x.Description, new BsonRegularExpression(escapedTerm, "i"))
            );
            return await _collection.Find(filter).ToListAsync(cancellationToken);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "SearchTenants");
    }

    public async Task<List<Tenant>> GetTenantsByCreatorAsync(string createdBy)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(tenant => tenant.CreatedBy == createdBy).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTenantsByCreator");
    }

    public async Task<bool> AddAgentAsync(string tenantId, Agent agent)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
            var update = Builders<Tenant>.Update
                .Push(t => t.Agents, agent)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddAgent");
    }

    public async Task<bool> UpdateAgentAsync(string tenantId, string agentName, Agent updatedAgent)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.And(
                Builders<Tenant>.Filter.Eq(t => t.Id, tenantId),
                Builders<Tenant>.Filter.ElemMatch(t => t.Agents, a => a.Name == agentName)
            );

            var update = Builders<Tenant>.Update
                .Set("agents.$", updatedAgent)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAgent");
    }

    public async Task<bool> RemoveAgentAsync(string tenantId, string agentName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.Eq(t => t.Id, tenantId);
            var update = Builders<Tenant>.Update
                .PullFilter(t => t.Agents, a => a.Name == agentName)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);

            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "RemoveAgent");
    }

    public async Task<bool> AddFlowToAgentAsync(string tenantId, string agentName, Flow flow)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<Tenant>.Filter.And(
                Builders<Tenant>.Filter.Eq(t => t.Id, tenantId),
                Builders<Tenant>.Filter.ElemMatch(t => t.Agents, a => a.Name == agentName)
            );
            var update = Builders<Tenant>.Update
                .Push("agents.$.flows", flow)
                .Set(t => t.UpdatedAt, DateTime.UtcNow);
            var result = await _collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddFlowToAgent");
    }
}