using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Shared.Utils.Services;
using XiansAi.Server.Shared.Repositories;

namespace XiansAi.Server.Shared.Services
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

        public ApiKeyService(IApiKeyRepository apiKeyRepository, ILogger<ApiKeyService> logger)
        {
            _apiKeyRepository = apiKeyRepository;
            _logger = logger;
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
                return ServiceResult<(string, ApiKey)>.Conflict($"API key name '{name}' already exists for this tenant.");
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
                var ok = await _apiKeyRepository.RevokeAsync(id, tenantId);
                if (!ok)
                    return ServiceResult<bool>.NotFound("API key not found.");
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
                var rotated = await _apiKeyRepository.RotateAsync(id, tenantId);
                if (rotated == null)
                    return ServiceResult<(string, ApiKey)?>.NotFound("API key not found.");
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

        // Do not change this method
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
