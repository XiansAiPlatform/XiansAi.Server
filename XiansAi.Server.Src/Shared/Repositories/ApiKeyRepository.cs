using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Security.Cryptography;
using System.Text;
using Shared.Data;
using Shared.Data.Models;

namespace XiansAi.Server.Shared.Repositories
{
    public class ApiKey
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = null!;

        [BsonElement("tenant_id")]
        public required string TenantId { get; set; }

        [BsonElement("name")]
        public required string Name { get; set; } // Label for the key

        [BsonElement("hashed_key")]
        public required string HashedKey { get; set; }

        [BsonElement("created_at")]
        public required DateTime CreatedAt { get; set; }

        [BsonElement("created_by")]
        public required string CreatedBy { get; set; }

        [BsonElement("revoked_at")]
        public DateTime? RevokedAt { get; set; }

        [BsonElement("last_rotated_at")]
        public DateTime? LastRotatedAt { get; set; }
    }

    public interface IApiKeyRepository
    {
        Task<(string apiKey, ApiKey meta)> CreateAsync(string tenantId, string name, string createdBy);
        Task<bool> RevokeAsync(string id, string tenantId);
        Task<List<ApiKey>> GetByTenantAsync(string tenantId, bool hasRevoked=false);
        Task<(string apiKey, ApiKey meta)?> RotateAsync(string id, string tenantId);
        Task<ApiKey?> GetByIdAsync(string id, string tenantId);
        Task<ApiKey?> GetByRawKeyAsync(string rawKey, string tenantId);
    }

    public class ApiKeyRepository : IApiKeyRepository
    {
        private readonly IMongoCollection<ApiKey> _collection;
        private readonly ILogger<ApiKeyRepository> _logger;
        private readonly Lazy<Task> _indexCreationTask;
        private volatile bool _indexesCreated = false;

        public ApiKeyRepository(IDatabaseService databaseService, ILogger<ApiKeyRepository> logger)
        {
            var database = databaseService.GetDatabase().Result;
            _collection = database.GetCollection<ApiKey>("api_keys");
            _logger = logger;
            _indexCreationTask = new Lazy<Task>(() => InitializeIndexesAsync());
            _ = Task.Run(async () =>
            {
                try { await _indexCreationTask.Value; }
                catch (Exception ex) { _logger.LogWarning(ex, "Background index creation failed during ApiKeyRepository initialization"); }
            });
        }

        private async Task InitializeIndexesAsync()
        {
            if (_indexesCreated) return;
            try
            {
                // Unique index on (tenant_id, name)
                var uniqueNameIndex = new CreateIndexModel<ApiKey>(
                    Builders<ApiKey>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Name),
                    new CreateIndexOptions { Unique = true, Name = "tenant_name_unique" }
                );
                // Index on tenant_id
                var tenantIndex = new CreateIndexModel<ApiKey>(
                    Builders<ApiKey>.IndexKeys.Ascending(x => x.TenantId),
                    new CreateIndexOptions { Name = "tenant_id_idx" }
                );
                // Index on hashed_key (for validation)
                var hashedKeyIndex = new CreateIndexModel<ApiKey>(
                    Builders<ApiKey>.IndexKeys.Ascending(x => x.HashedKey),
                    new CreateIndexOptions { Name = "hashed_key_idx" }
                );
                await _collection.Indexes.CreateManyAsync(new[] { uniqueNameIndex, tenantIndex, hashedKeyIndex });
                _indexesCreated = true;
                _logger.LogInformation("Successfully created indexes for ApiKey collection");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create indexes for ApiKey collection, will retry on next operation");
            }
        }

        private async Task EnsureIndexesAsync()
        {
            if (!_indexesCreated)
            {
                try { await _indexCreationTask.Value; }
                catch (Exception ex) { _logger.LogDebug(ex, "Index creation not yet complete, continuing with operation"); }
            }
        }

        public async Task<(string apiKey, ApiKey meta)> CreateAsync(string tenantId, string name, string createdBy)
        {
            try
            {
                await EnsureIndexesAsync();
                var apiKey = GenerateApiKey();
                var hashedKey = HashApiKey(apiKey);
                var now = DateTime.UtcNow;
                var doc = new ApiKey
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TenantId = tenantId,
                    Name = name,
                    HashedKey = hashedKey,
                    CreatedAt = now,
                    CreatedBy = createdBy,
                    RevokedAt = null,
                    LastRotatedAt = null
                };
                await _collection.InsertOneAsync(doc);
                return (apiKey, doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating API key for tenant {TenantId}", tenantId);
                throw;
            }
        }

        public async Task<bool> RevokeAsync(string id, string tenantId)
        {
            try
            {
                await EnsureIndexesAsync();
                var update = Builders<ApiKey>.Update.Set(x => x.RevokedAt, DateTime.UtcNow);
                var result = await _collection.UpdateOneAsync(
                    x => x.Id == id && x.TenantId == tenantId && x.RevokedAt == null, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return false;
            }
        }

        public async Task<List<ApiKey>> GetByTenantAsync(string tenantId, bool hasRevoked = false)
        {
            try
            {
                await EnsureIndexesAsync();
                if (hasRevoked)
                {
                    return await _collection.Find(x => x.TenantId == tenantId).ToListAsync();
                }
                return await _collection.Find(x => x.TenantId == tenantId && x.RevokedAt == null).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API keys for tenant {TenantId}", tenantId);
                return new List<ApiKey>();
            }
        }

        public async Task<(string apiKey, ApiKey meta)?> RotateAsync(string id, string tenantId)
        {
            try
            {
                await EnsureIndexesAsync();
                var apiKey = GenerateApiKey();
                var hashedKey = HashApiKey(apiKey);
                var now = DateTime.UtcNow;
                var update = Builders<ApiKey>.Update
                    .Set(x => x.HashedKey, hashedKey)
                    .Set(x => x.LastRotatedAt, now)
                    .Set(x => x.RevokedAt, null);
                var result = await _collection.FindOneAndUpdateAsync(
                    x => x.Id == id && x.TenantId == tenantId && x.RevokedAt == null,
                    update,
                    new FindOneAndUpdateOptions<ApiKey> { ReturnDocument = ReturnDocument.After });
                if (result == null) return null;
                return (apiKey, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rotating API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return null;
            }
        }

        public async Task<ApiKey?> GetByIdAsync(string id, string tenantId)
        {
            try
            {
                await EnsureIndexesAsync();
                return await _collection.Find(x => x.Id == id && x.TenantId == tenantId).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return null;
            }
        }

        public async Task<ApiKey?> GetByRawKeyAsync(string rawKey, string tenantId)
        {
            try
            {
                await EnsureIndexesAsync();
                var hashed = HashApiKey(rawKey);
                return await _collection.Find(x => x.HashedKey == hashed && x.TenantId == tenantId && x.RevokedAt == null).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by raw key for tenant {TenantId}", tenantId);
                return null;
            }
        }

        private static string GenerateApiKey()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string HashApiKey(string apiKey)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hash);
        }
    }
}
