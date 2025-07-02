using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using XiansAi.Server.Shared.Repositories;

namespace XiansAi.Server.Shared.Services
{
    public interface IApiKeyService
    {
        Task<(string apiKey, ApiKey meta)> CreateApiKeyAsync(string tenantId, string name, string createdBy);
        Task<bool> RevokeApiKeyAsync(string id, string tenantId);
        Task<List<ApiKey>> GetApiKeysAsync(string tenantId);
        Task<(string apiKey, ApiKey meta)?> RotateApiKeyAsync(string id, string tenantId);
        Task<ApiKey?> GetApiKeyByIdAsync(string id, string tenantId);
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

        public ApiKeyService(IApiKeyRepository apiKeyRepository, ILogger<ApiKeyService> logger)
        {
            _apiKeyRepository = apiKeyRepository;
            _logger = logger;
        }

        public async Task<(string apiKey, ApiKey meta)> CreateApiKeyAsync(string tenantId, string name, string createdBy)
        {
            _logger.LogInformation("Creating API key for tenant {TenantId} by {CreatedBy}", tenantId, createdBy);
            try
            {
                return await _apiKeyRepository.CreateAsync(tenantId, name, createdBy);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning("Duplicate API key name '{Name}' for tenant {TenantId}", name, tenantId);
                throw new DuplicateApiKeyNameException($"API key name '{name}' already exists for this tenant.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating API key for tenant {TenantId}", tenantId);
                throw;
            }
        }

        public async Task<bool> RevokeApiKeyAsync(string id, string tenantId)
        {
            _logger.LogInformation("Revoking API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
            try
            {
                return await _apiKeyRepository.RevokeAsync(id, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                throw;
            }
        }

        public async Task<List<ApiKey>> GetApiKeysAsync(string tenantId)
        {
            _logger.LogInformation("Getting API keys for tenant {TenantId}", tenantId);
            try
            {
                return await _apiKeyRepository.GetByTenantAsync(tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API keys for tenant {TenantId}", tenantId);
                throw;
            }
        }

        public async Task<(string apiKey, ApiKey meta)?> RotateApiKeyAsync(string id, string tenantId)
        {
            _logger.LogInformation("Rotating API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
            try
            {
                return await _apiKeyRepository.RotateAsync(id, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rotating API key {ApiKeyId} for tenant {TenantId}", id, tenantId);
                throw;
            }
        }

        public async Task<ApiKey?> GetApiKeyByIdAsync(string id, string tenantId)
        {
            try
            {
                return await _apiKeyRepository.GetByIdAsync(id, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by id {ApiKeyId} for tenant {TenantId}", id, tenantId);
                throw;
            }
        }

        public async Task<ApiKey?> GetApiKeyByRawKeyAsync(string rawKey, string tenantId)
        {
            try
            {
                return await _apiKeyRepository.GetByRawKeyAsync(rawKey, tenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API key by raw key for tenant {TenantId}", tenantId);
                throw;
            }
        }
    }
}
