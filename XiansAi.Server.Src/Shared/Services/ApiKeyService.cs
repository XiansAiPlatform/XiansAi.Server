using MongoDB.Driver;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Data.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
namespace Shared.Services
{
    public interface IApiKeyService
    {
        Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(string tenantId, string name, string createdBy);
        Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId);
        Task<ServiceResult<List<ApiKey>>> GetApiKeysAsync(string tenantId);
        Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(string id, string tenantId);
        Task<ServiceResult<ApiKey?>> GetApiKeyByIdAsync(string id, string tenantId);
        Task<ApiKey?> GetApiKeyByRawKeyAsync(string rawKey, string tenantId);
    }
    public class DuplicateApiKeyNameException : Exception
    {
        public DuplicateApiKeyNameException(string message) : base(message) { }
    }
    public class ApiKeyService : IApiKeyService
    {
        private readonly IApiKeyRepository _apiKeyRepository;
        private readonly ILogger<ApiKeyService> _logger;
        private readonly IMemoryCache _cache;
        
        // Cache configuration
        private static readonly TimeSpan ApiKeyCacheExpiration = TimeSpan.FromMinutes(15);
        private static readonly string CacheKeyPrefix = "apikey:";

        public ApiKeyService(IApiKeyRepository apiKeyRepository, ILogger<ApiKeyService> logger, IMemoryCache cache)
        {
            _apiKeyRepository = apiKeyRepository;
            _logger = logger;
            _cache = cache;
        }

        public async Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(string tenantId, string name, string createdBy)
        {
            _logger.LogInformation("Creating API key for tenant {TenantId} by {CreatedBy}", tenantId, createdBy);
            try
            {
                var result = await _apiKeyRepository.CreateAsync(tenantId, name, createdBy);
                return ServiceResult<(string, ApiKey)>.Success(result);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning("Duplicate API key name '{Name}' for tenant {TenantId}", name, tenantId);
                return ServiceResult<(string, ApiKey)>.Conflict($"API key name '{name}' was previously created for this tenant, use another name.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating API key for tenant {TenantId}", tenantId);
                return ServiceResult<(string, ApiKey)>.InternalServerError("An error occurred while creating the API key. " + ex.Message);
            }
        }

        public async Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId)
        {
            _logger.LogInformation("Revoking API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
            try
            {
                // Get the API key first to invalidate its cache entry
                var existingKey = await _apiKeyRepository.GetByIdAsync(id, tenantId);
                
                var ok = await _apiKeyRepository.RevokeAsync(id, tenantId);
                if (!ok)
                    return ServiceResult<bool>.NotFound("API key not found.");
                
                // Invalidate cache entry if the key existed
                if (existingKey != null)
                {
                    var cacheKey = $"{CacheKeyPrefix}{tenantId}:{existingKey.HashedKey}";
                    _cache.Remove(cacheKey);
                    _logger.LogDebug("Invalidated cache for revoked API key {ApiKeyId} in tenant {TenantId}", id, tenantId);
                }
                
                return ServiceResult<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return ServiceResult<bool>.InternalServerError("An error occurred while revoking the API key. " + ex.Message);
            }
        }

        public async Task<ServiceResult<List<ApiKey>>> GetApiKeysAsync(string tenantId)
        {
            _logger.LogInformation("Getting API keys for tenant {TenantId}", tenantId);
            try
            {
                var keys = await _apiKeyRepository.GetByTenantAsync(tenantId);
                return ServiceResult<List<ApiKey>>.Success(keys);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API keys for tenant {TenantId}", tenantId);
                return ServiceResult<List<ApiKey>>.InternalServerError("An error occurred while retrieving API keys. " + ex.Message);
            }
        }

        public async Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(string id, string tenantId)
        {
            _logger.LogInformation("Rotating API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
            try
            {
                // Get the existing API key to invalidate its cache entry
                var existingKey = await _apiKeyRepository.GetByIdAsync(id, tenantId);
                
                var rotated = await _apiKeyRepository.RotateAsync(id, tenantId);
                if (rotated == null)
                    return ServiceResult<(string, ApiKey)?>.NotFound("API key not found.");
                
                // Invalidate old cache entry if the key existed
                if (existingKey != null)
                {
                    var oldCacheKey = $"{CacheKeyPrefix}{tenantId}:{existingKey.HashedKey}";
                    _cache.Remove(oldCacheKey);
                    _logger.LogDebug("Invalidated old cache for rotated API key {ApiKeyId} in tenant {TenantId}", id, tenantId);
                }
                
                // The new key will be cached on first use by GetApiKeyByRawKeyAsync
                
                return ServiceResult<(string, ApiKey)?>.Success(rotated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rotating API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return ServiceResult<(string, ApiKey)?>.InternalServerError("An error occurred while rotating the API key. " + ex.Message);
            }
        }

        public async Task<ServiceResult<ApiKey?>> GetApiKeyByIdAsync(string id, string tenantId)
        {
            try
            {
                var key = await _apiKeyRepository.GetByIdAsync(id, tenantId);
                if (key == null)
                    return ServiceResult<ApiKey?>.NotFound("API key not found.");
                return ServiceResult<ApiKey?>.Success(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by id {ApiKeyId} for tenant {TenantId}", id, tenantId);
                return ServiceResult<ApiKey?>.InternalServerError("An error occurred while retrieving the API key. " + ex.Message);
            }
        }

        // Enhanced with caching for performance - preserving original behavior
        public async Task<ApiKey?> GetApiKeyByRawKeyAsync(string rawKey, string tenantId)
        {
            try
            {
                // Generate cache key using hashed API key for security
                var hashedKey = HashApiKey(rawKey);
                var cacheKey = $"{CacheKeyPrefix}{tenantId}:{hashedKey}";
                
                // Try to get from cache first
                if (_cache.TryGetValue(cacheKey, out ApiKey? cachedApiKey))
                {
                    _logger.LogDebug("Retrieved API key for tenant {TenantId} from cache", tenantId);
                    return cachedApiKey;
                }
                
                // Cache miss - fetch from database
                _logger.LogDebug("Cache miss for tenant {TenantId} API key, fetching from database", tenantId);
                var apiKey = await _apiKeyRepository.GetByRawKeyAsync(rawKey, tenantId);
                
                // Cache the result (including null results to prevent repeated DB hits for invalid keys)
                if (apiKey != null)
                {
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(ApiKeyCacheExpiration)
                        .SetSize(1);
                    _cache.Set(cacheKey, apiKey, cacheOptions);
                    _logger.LogDebug("Cached API key for tenant {TenantId} with {CacheExpiration} expiration", 
                        tenantId, ApiKeyCacheExpiration);
                }
                else
                {
                    // Cache null results with shorter TTL to prevent brute force attacks
                    var cacheOptions = new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(2))
                        .SetSize(1);
                    _cache.Set(cacheKey, (ApiKey?)null, cacheOptions);
                    _logger.LogDebug("Cached null API key result for tenant {TenantId}", tenantId);
                }
                
                return apiKey;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by raw key for tenant {TenantId}", tenantId);
                throw;
            }
        }
        
        /// <summary>
        /// Hashes an API key for secure caching and storage
        /// </summary>
        /// <param name="apiKey">The raw API key to hash</param>
        /// <returns>Base64 encoded SHA256 hash of the API key</returns>
        private static string HashApiKey(string apiKey)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
            return Convert.ToBase64String(hash);
        }
    }
}
