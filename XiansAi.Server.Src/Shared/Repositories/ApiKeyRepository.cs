using MongoDB.Driver;
using MongoDB.Bson;
using System.Security.Cryptography;
using System.Text;
using Shared.Data;
using Shared.Data.Models;

namespace Shared.Repositories
{
    public interface IApiKeyRepository
    {
        Task<(string apiKey, ApiKey meta)> CreateAsync(string tenantId, string name, string createdBy);
        Task<bool> RevokeAsync(string id, string tenantId);
        Task<List<ApiKey>> GetByTenantAsync(string tenantId, bool hasRevoked=false);
        Task<(string apiKey, ApiKey meta)?> RotateAsync(string id, string tenantId);
        Task<ApiKey?> GetByIdAsync(string id, string tenantId);
        Task<ApiKey?> GetByRawKeyAsync(string rawKey, string tenantId);
        Task<ApiKey?> GetByRawKeyAsync(string rawKey); // Overload without tenantId for authentication
    }

    public class ApiKeyRepository : IApiKeyRepository
    {
        private readonly IMongoCollection<ApiKey> _collection;
        private readonly ILogger<ApiKeyRepository> _logger;

        public ApiKeyRepository(IDatabaseService databaseService, ILogger<ApiKeyRepository> logger)
        {
            var database = databaseService.GetDatabaseAsync().Result;
            _collection = database.GetCollection<ApiKey>("api_keys");
            _logger = logger;
        }



        public async Task<(string apiKey, ApiKey meta)> CreateAsync(string tenantId, string name, string createdBy)
        {
            try
            {
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
                var hashed = HashApiKey(rawKey);
                return await _collection.Find(x => x.HashedKey == hashed && x.TenantId == tenantId && x.RevokedAt == null).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by raw key for tenant {TenantId}", tenantId);
                return null;
            }
        }

        public async Task<ApiKey?> GetByRawKeyAsync(string rawKey)
        {
            try
            {
                var hashed = HashApiKey(rawKey);
                return await _collection.Find(x => x.HashedKey == hashed && x.RevokedAt == null).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by raw key");
                return null;
            }
        }

        private static string GenerateApiKey()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return "sk-Xnai-"+Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        private static string HashApiKey(string apiKey)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hash);
        }
    }
}
