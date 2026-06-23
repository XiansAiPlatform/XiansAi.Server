using Shared.Data.Models;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Admin-facing service for managing named API keys scoped to the requesting user.
/// All list/get/revoke/rotate operations are restricted to keys whose
/// <c>created_by</c> field matches the caller's user ID.
/// Agent certificates are managed separately via <see cref="CertificateService"/>.
/// </summary>
public interface IAdminApiKeyService
{
    /// <summary>Creates a named API key attributed to <paramref name="userId"/>.</summary>
    Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(
        string tenantId, string name, string userId);

    /// <summary>Lists only the API keys created by <paramref name="userId"/> within the tenant.</summary>
    Task<ServiceResult<List<ApiKey>>> ListApiKeysAsync(string tenantId, string userId);

    /// <summary>Gets a key by ID, returning it only if it was created by <paramref name="userId"/>.</summary>
    Task<ServiceResult<ApiKey?>> GetApiKeyAsync(string id, string tenantId, string userId);

    /// <summary>Revokes a key only if it was created by <paramref name="userId"/>.</summary>
    Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId, string userId);

    /// <summary>Rotates a key only if it was created by <paramref name="userId"/>.</summary>
    Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(string id, string tenantId, string userId);
}

public class AdminApiKeyService : IAdminApiKeyService
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<AdminApiKeyService> _logger;

    public AdminApiKeyService(IApiKeyService apiKeyService, ILogger<AdminApiKeyService> logger)
    {
        _apiKeyService = apiKeyService;
        _logger = logger;
    }

    public async Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(
        string tenantId, string name, string userId)
    {
        _logger.LogInformation(
            "Creating API key '{Name}' for tenant {TenantId} by user {UserId}",
            LogSanitizer.Sanitize(name), LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(userId));

        return await _apiKeyService.CreateApiKeyAsync(tenantId, name, userId);
    }

    public async Task<ServiceResult<List<ApiKey>>> ListApiKeysAsync(string tenantId, string userId)
    {
        var result = await _apiKeyService.GetApiKeysAsync(tenantId);
        if (!result.IsSuccess || result.Data == null)
            return result;

        var owned = result.Data
            .Where(k => string.Equals(k.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return ServiceResult<List<ApiKey>>.Success(owned);
    }

    public async Task<ServiceResult<ApiKey?>> GetApiKeyAsync(string id, string tenantId, string userId)
    {
        var result = await _apiKeyService.GetApiKeyByIdAsync(id, tenantId);
        if (!result.IsSuccess || result.Data == null)
            return result;

        if (!string.Equals(result.Data.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "User {UserId} attempted to access API key {KeyId} owned by {Owner}",
                LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(id),
                LogSanitizer.Sanitize(result.Data.CreatedBy));
            return ServiceResult<ApiKey?>.NotFound("API key not found.");
        }

        return result;
    }

    public async Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId, string userId)
    {
        var ownerCheck = await GetApiKeyAsync(id, tenantId, userId);
        if (!ownerCheck.IsSuccess || ownerCheck.Data == null)
            return ServiceResult<bool>.NotFound("API key not found.");

        return await _apiKeyService.RevokeApiKeyAsync(id, tenantId);
    }

    public async Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(
        string id, string tenantId, string userId)
    {
        var ownerCheck = await GetApiKeyAsync(id, tenantId, userId);
        if (!ownerCheck.IsSuccess || ownerCheck.Data == null)
            return ServiceResult<(string, ApiKey)?>.NotFound("API key not found.");

        return await _apiKeyService.RotateApiKeyAsync(id, tenantId);
    }
}
