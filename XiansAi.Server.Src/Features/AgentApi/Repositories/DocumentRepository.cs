using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Text.Json;
using Shared.Data.Models;
using Shared.Data;
using Shared.Utils;

namespace Features.AgentApi.Repositories;

public interface IDocumentRepository
{
    Task<Document> CreateAsync(Document document);
    Task<Document?> GetByIdAsync(string id);
    Task<Document?> GetByKeyAsync(string type, string key, string? tenantId);
    Task<List<Document>> QueryAsync(string? tenantId, DocumentQueryFilter filter);
    Task<long> CountAsync(string? tenantId, DocumentQueryFilter filter);
    Task<List<string>> GetDistinctTypesAsync(string? tenantId, string? agentId);
    Task<List<string>> GetDistinctActivationNamesAsync(string? tenantId, string? agentId);
    Task<bool> UpdateAsync(Document document);
    Task<bool> DeleteAsync(string id, string? tenantId);
    Task<int> DeleteManyAsync(IEnumerable<string> ids, string? tenantId);
    Task<int> DeleteManyByIdsAndAgentsAsync(IEnumerable<string> ids, IEnumerable<string> agentNames, string? tenantId);
    Task<bool> ExistsAsync(string id, string? tenantId);
    Task<bool> ExistsByKeyAsync(string type, string key, string? tenantId);
}

public class DocumentQueryFilter
{
    public string? AgentId { get; set; }
    public List<string>? AgentIds { get; set; }
    public string? Type { get; set; }
    public string? Key { get; set; }
    public string? ParticipantId { get; set; }
    public string? ActivationName { get; set; }
    public Dictionary<string, object>? MetadataFilters { get; set; }
    public int Limit { get; set; } = 100;
    public int Skip { get; set; } = 0;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public string? ContentType { get; set; }
}

public class DocumentRepository : IDocumentRepository
{
    private readonly IMongoCollection<Document> _documents;
    private readonly ILogger<DocumentRepository> _logger;

    public DocumentRepository(
        IDatabaseService databaseService,
        ILogger<DocumentRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _documents = database.GetCollection<Document>("documents");
        _logger = logger;
    }

    public async Task<Document> CreateAsync(Document document)
    {
        try
        {
            document.Id = ObjectId.GenerateNewId().ToString();
            document.CreatedAt = DateTime.UtcNow;
            
            await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.InsertOneAsync(document),
                _logger);
            
            return document;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating document");
            throw;
        }
    }

    public async Task<Document?> GetByIdAsync(string id)
    {
        try
        {
            var filter = Builders<Document>.Filter.Eq(d => d.Id, id);
            
            return await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.Find(filter).FirstOrDefaultAsync(),
                _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document with ID: {Id}", id);
            throw;
        }
    }

    public async Task<Document?> GetByKeyAsync(string type, string key, string? tenantId)
    {
        try
        {
            var builder = Builders<Document>.Filter;
            var filter = builder.Eq(d => d.Type, type) & builder.Eq(d => d.Key, key);
            
            // Add tenant filter if provided
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= builder.Eq(d => d.TenantId, tenantId);
            }
            
            return await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.Find(filter).FirstOrDefaultAsync(),
                _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document with Type: {Type} and Key: {Key}", type, key);
            throw;
        }
    }

    public async Task<List<Document>> QueryAsync(string? tenantId, DocumentQueryFilter queryFilter)
    {
        try
        {
            var filter = BuildFilter(tenantId, queryFilter);

            // Create find options with sort and pagination
            var sortField = queryFilter.SortBy switch
            {
                "UpdatedAt" => "updated_at",
                "CreatedAt" => "created_at",
                null => "created_at",
                _ => "created_at"
            };

            var sort = queryFilter.SortDescending 
                ? Builders<Document>.Sort.Descending(sortField)
                : Builders<Document>.Sort.Ascending(sortField);

            var cursor = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.FindAsync(filter, new FindOptions<Document>
                {
                    Sort = sort,
                    Skip = queryFilter.Skip,
                    Limit = queryFilter.Limit
                }),
                _logger);

            return await cursor.ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying documents");
            throw;
        }
    }

    /// <summary>
    /// Builds a MongoDB filter from the query filter parameters.
    /// Extracted to a helper method to ensure consistency between Query and Count operations.
    /// </summary>
    private FilterDefinition<Document> BuildFilter(string? tenantId, DocumentQueryFilter queryFilter)
    {
        var builder = Builders<Document>.Filter;
        var filter = builder.Empty;

        // Add tenant filter if provided
        if (!string.IsNullOrEmpty(tenantId))
        {
            filter &= builder.Eq(d => d.TenantId, tenantId);
        }

        // Add agent filter - support both single AgentId and multiple AgentIds
        if (queryFilter.AgentIds != null && queryFilter.AgentIds.Any())
        {
            filter &= builder.In(d => d.AgentId, queryFilter.AgentIds);
        }
        else if (!string.IsNullOrEmpty(queryFilter.AgentId))
        {
            filter &= builder.Eq(d => d.AgentId, queryFilter.AgentId);
        }

        // Add type filter
        if (!string.IsNullOrEmpty(queryFilter.Type))
        {
            filter &= builder.Eq(d => d.Type, queryFilter.Type);
        }

        // Add key filter
        if (!string.IsNullOrEmpty(queryFilter.Key))
        {
            filter &= builder.Eq(d => d.Key, queryFilter.Key);
        }

        // Add participant filter
        if (!string.IsNullOrEmpty(queryFilter.ParticipantId))
        {
            filter &= builder.Eq(d => d.ParticipantId, queryFilter.ParticipantId);
        }

        // Add activation name filter
        if (!string.IsNullOrEmpty(queryFilter.ActivationName))
        {
            filter &= builder.Eq(d => d.ActivationName, queryFilter.ActivationName);
        }

        // Add content type filter
        if (!string.IsNullOrEmpty(queryFilter.ContentType))
        {
            filter &= builder.Eq(d => d.ContentType, queryFilter.ContentType);
        }

        // Add metadata filters
        if (queryFilter.MetadataFilters != null && queryFilter.MetadataFilters.Any())
        {
            foreach (var kvp in queryFilter.MetadataFilters)
            {
                // Convert JsonElement to BsonValue if needed
                var filterValue = kvp.Value is JsonElement jsonElement
                    ? ConvertJsonElementToBsonValue(jsonElement)
                    : kvp.Value;
                
                filter &= builder.Eq($"metadata.{kvp.Key}", filterValue);
            }
        }

        // Add date filters
        if (queryFilter.CreatedAfter.HasValue)
        {
            filter &= builder.Gte(d => d.CreatedAt, queryFilter.CreatedAfter.Value);
        }
        if (queryFilter.CreatedBefore.HasValue)
        {
            filter &= builder.Lte(d => d.CreatedAt, queryFilter.CreatedBefore.Value);
        }

        return filter;
    }

    public async Task<long> CountAsync(string? tenantId, DocumentQueryFilter queryFilter)
    {
        try
        {
            var filter = BuildFilter(tenantId, queryFilter);

            return await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.CountDocumentsAsync(filter),
                _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting documents");
            throw;
        }
    }

    public async Task<List<string>> GetDistinctTypesAsync(string? tenantId, string? agentId)
    {
        try
        {
            var builder = Builders<Document>.Filter;
            var filter = builder.Empty;

            // Add tenant filter if provided
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= builder.Eq(d => d.TenantId, tenantId);
            }

            // Add agent filter if provided
            if (!string.IsNullOrEmpty(agentId))
            {
                filter &= builder.Eq(d => d.AgentId, agentId);
            }

            // Use MongoDB's Distinct operation for efficiency
            var distinctTypes = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.DistinctAsync<string>("type", filter),
                _logger);

            var types = await distinctTypes.ToListAsync();
            
            // Filter out null/empty and sort
            return types
                .Where(t => !string.IsNullOrEmpty(t))
                .OrderBy(t => t)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting distinct document types for tenant {TenantId}, agent {AgentId}", 
                tenantId, agentId);
            throw;
        }
    }

    public async Task<List<string>> GetDistinctActivationNamesAsync(string? tenantId, string? agentId)
    {
        try
        {
            var builder = Builders<Document>.Filter;
            var filter = builder.Empty;

            // Add tenant filter if provided
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= builder.Eq(d => d.TenantId, tenantId);
            }

            // Add agent filter if provided
            if (!string.IsNullOrEmpty(agentId))
            {
                filter &= builder.Eq(d => d.AgentId, agentId);
            }

            // Use MongoDB's Distinct operation for efficiency
            var distinctActivationNames = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.DistinctAsync<string>("activation_name", filter),
                _logger);

            var activationNames = await distinctActivationNames.ToListAsync();
            
            // Filter out null/empty and sort
            return activationNames
                .Where(a => !string.IsNullOrEmpty(a))
                .OrderBy(a => a)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting distinct activation names for tenant {TenantId}, agent {AgentId}", 
                tenantId, agentId);
            throw;
        }
    }

    public async Task<bool> UpdateAsync(Document document)
    {
        try
        {
            document.UpdatedAt = DateTime.UtcNow;
            
            var filter = Builders<Document>.Filter.Eq(d => d.Id, document.Id);
            
            // If tenant is specified, add it to the filter for security
            if (!string.IsNullOrEmpty(document.TenantId))
            {
                filter &= Builders<Document>.Filter.Eq(d => d.TenantId, document.TenantId);
            }

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.ReplaceOneAsync(filter, document),
                _logger);

            return result.MatchedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating document with ID: {Id}", document.Id);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id, string? tenantId)
    {
        try
        {
            var filter = Builders<Document>.Filter.Eq(d => d.Id, id);
            
            // Add tenant filter if provided for security
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= Builders<Document>.Filter.Eq(d => d.TenantId, tenantId);
            }

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.DeleteOneAsync(filter),
                _logger);

            return result.DeletedCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document with ID: {Id}", id);
            throw;
        }
    }

    public async Task<int> DeleteManyAsync(IEnumerable<string> ids, string? tenantId)
    {
        try
        {
            var idList = ids.ToList();
            if (!idList.Any())
            {
                return 0;
            }

            var filter = Builders<Document>.Filter.In(d => d.Id, idList);
            
            // Add tenant filter if provided for security
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= Builders<Document>.Filter.Eq(d => d.TenantId, tenantId);
            }

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.DeleteManyAsync(filter),
                _logger);

            return (int)result.DeletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting multiple documents");
            throw;
        }
    }

    public async Task<int> DeleteManyByIdsAndAgentsAsync(IEnumerable<string> ids, IEnumerable<string> agentNames, string? tenantId)
    {
        try
        {
            var idList = ids.ToList();
            var agentList = agentNames.ToList();
            
            if (!idList.Any() || !agentList.Any())
            {
                return 0;
            }

            var builder = Builders<Document>.Filter;
            var filter = builder.And(
                builder.In(d => d.Id, idList),
                builder.In(d => d.AgentId, agentList)
            );
            
            // Add tenant filter if provided for security
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= builder.Eq(d => d.TenantId, tenantId);
            }

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.DeleteManyAsync(filter),
                _logger);

            return (int)result.DeletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting multiple documents by IDs and agents");
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string id, string? tenantId)
    {
        try
        {
            var filter = Builders<Document>.Filter.Eq(d => d.Id, id);
            
            // Add tenant filter if provided for security
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= Builders<Document>.Filter.Eq(d => d.TenantId, tenantId);
            }

            var count = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.CountDocumentsAsync(filter),
                _logger);

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking document existence with ID: {Id}", id);
            throw;
        }
    }

    public async Task<bool> ExistsByKeyAsync(string type, string key, string? tenantId)
    {
        try
        {
            var builder = Builders<Document>.Filter;
            var filter = builder.Eq(d => d.Type, type) & builder.Eq(d => d.Key, key);
            
            // Add tenant filter if provided for security
            if (!string.IsNullOrEmpty(tenantId))
            {
                filter &= builder.Eq(d => d.TenantId, tenantId);
            }

            var count = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _documents.CountDocumentsAsync(filter),
                _logger);

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking document existence with Type: {Type} and Key: {Key}", type, key);
            throw;
        }
    }

    /// <summary>
    /// Converts a JsonElement to a BsonValue for use in MongoDB queries.
    /// </summary>
    private static BsonValue ConvertJsonElementToBsonValue(JsonElement element)
    {
        var json = JsonSerializer.Serialize(element);
        return BsonSerializer.Deserialize<BsonValue>(json);
    }
}
