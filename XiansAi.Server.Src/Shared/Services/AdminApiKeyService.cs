using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// The account an admin API key belongs to.
/// </summary>
/// <param name="UserId">
/// The account's <see cref="User.UserId"/>, which is what new keys store in <c>created_by</c> so that
/// admin API key authentication resolves the owner's roles by id. Falls back to the value the caller
/// supplied when no account matches it, so keys left by a removed account can still be listed and revoked.
/// </param>
/// <param name="OwnedKeyCreators">
/// Every <c>created_by</c> value that counts as this account: the user id, plus the account's email when
/// no other account holds it, since keys created before <c>created_by</c> stored the user id hold the email.
/// </param>
/// <param name="Email">The account's email, or null when no account matched.</param>
public sealed record ApiKeyOwner(string UserId, IReadOnlyList<string> OwnedKeyCreators, string? Email = null)
{
    public bool Owns(ApiKey key) =>
        OwnedKeyCreators.Any(creator => string.Equals(creator, key.CreatedBy, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="userIdOrEmail"/> names this account, by user id or email.</summary>
    public bool IsNamedBy(string? userIdOrEmail)
    {
        var value = userIdOrEmail?.Trim();
        if (string.IsNullOrEmpty(value))
            return false;

        return string.Equals(value, UserId, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(Email) && string.Equals(value, Email, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Admin-facing service for managing named API keys scoped to the requesting user.
/// All list/get/revoke/rotate operations are restricted to keys owned by the resolved <see cref="ApiKeyOwner"/>.
/// Agent certificates are managed separately via <see cref="CertificateService"/>.
/// </summary>
public interface IAdminApiKeyService
{
    /// <summary>Resolves a user id or email to the account whose keys are being managed.</summary>
    Task<ApiKeyOwner> ResolveOwnerAsync(string userIdOrEmail);

    /// <summary>Creates a named API key attributed to <paramref name="owner"/>'s user id.</summary>
    Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(
        string tenantId, string name, ApiKeyOwner owner);

    /// <summary>Lists only the API keys owned by <paramref name="owner"/> within the tenant.</summary>
    Task<ServiceResult<List<ApiKey>>> ListApiKeysAsync(string tenantId, ApiKeyOwner owner);

    /// <summary>Gets a key by ID, returning it only if it is owned by <paramref name="owner"/>.</summary>
    Task<ServiceResult<ApiKey?>> GetApiKeyAsync(string id, string tenantId, ApiKeyOwner owner);

    /// <summary>Revokes a key only if it is owned by <paramref name="owner"/>.</summary>
    Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId, ApiKeyOwner owner);

    /// <summary>Rotates a key only if it is owned by <paramref name="owner"/>.</summary>
    Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(string id, string tenantId, ApiKeyOwner owner);
}

public class AdminApiKeyService : IAdminApiKeyService
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AdminApiKeyService> _logger;

    public AdminApiKeyService(
        IApiKeyService apiKeyService,
        IUserRepository userRepository,
        ILogger<AdminApiKeyService> logger)
    {
        _apiKeyService = apiKeyService;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ApiKeyOwner> ResolveOwnerAsync(string userIdOrEmail)
    {
        var supplied = userIdOrEmail?.Trim() ?? string.Empty;
        var user = await _userRepository.GetByUserIdOrEmailAsync(supplied);
        if (user == null)
        {
            _logger.LogWarning("No account matches API key owner {Owner}; using the supplied value as-is",
                LogSanitizer.RedactUserId(supplied));
            return new ApiKeyOwner(supplied, new[] { supplied });
        }

        var creators = new List<string> { user.UserId };
        if (await IsLegacyEmailCreatorAsync(user))
        {
            creators.Add(user.Email);
        }

        return new ApiKeyOwner(user.UserId, creators, user.Email);
    }

    /// <summary>
    /// Whether keys whose <c>created_by</c> is this account's email belong to it. Not when another
    /// account holds the same address, since such a key cannot say which of them created it.
    /// </summary>
    private async Task<bool> IsLegacyEmailCreatorAsync(User user)
    {
        if (string.IsNullOrWhiteSpace(user.Email) ||
            string.Equals(user.Email, user.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !await _userRepository.IsEmailSharedAsync(user.Email, user.UserId);
    }

    public async Task<ServiceResult<(string apiKey, ApiKey meta)>> CreateApiKeyAsync(
        string tenantId, string name, ApiKeyOwner owner)
    {
        _logger.LogInformation(
            "Creating API key '{Name}' for tenant {TenantId} by user {UserId}",
            LogSanitizer.Sanitize(name), LogSanitizer.Sanitize(tenantId), LogSanitizer.RedactUserId(owner.UserId));

        return await _apiKeyService.CreateApiKeyAsync(tenantId, name, owner.UserId);
    }

    public async Task<ServiceResult<List<ApiKey>>> ListApiKeysAsync(string tenantId, ApiKeyOwner owner)
    {
        var result = await _apiKeyService.GetApiKeysAsync(tenantId);
        if (!result.IsSuccess || result.Data == null)
            return result;

        var owned = result.Data.Where(owner.Owns).ToList();

        return ServiceResult<List<ApiKey>>.Success(owned);
    }

    public async Task<ServiceResult<ApiKey?>> GetApiKeyAsync(string id, string tenantId, ApiKeyOwner owner)
    {
        var result = await _apiKeyService.GetApiKeyByIdAsync(id, tenantId);
        if (!result.IsSuccess || result.Data == null)
            return result;

        if (!owner.Owns(result.Data))
        {
            _logger.LogWarning(
                "User {UserId} attempted to access API key {KeyId} owned by {Owner}",
                LogSanitizer.RedactUserId(owner.UserId), LogSanitizer.Sanitize(id),
                LogSanitizer.RedactUserId(result.Data.CreatedBy));
            return ServiceResult<ApiKey?>.NotFound("API key not found.");
        }

        return result;
    }

    public async Task<ServiceResult<bool>> RevokeApiKeyAsync(string id, string tenantId, ApiKeyOwner owner)
    {
        var ownerCheck = await GetApiKeyAsync(id, tenantId, owner);
        if (!ownerCheck.IsSuccess || ownerCheck.Data == null)
            return ServiceResult<bool>.NotFound("API key not found.");

        return await _apiKeyService.RevokeApiKeyAsync(id, tenantId);
    }

    public async Task<ServiceResult<(string apiKey, ApiKey meta)?>> RotateApiKeyAsync(
        string id, string tenantId, ApiKeyOwner owner)
    {
        var ownerCheck = await GetApiKeyAsync(id, tenantId, owner);
        if (!ownerCheck.IsSuccess || ownerCheck.Data == null)
            return ServiceResult<(string, ApiKey)?>.NotFound("API key not found.");

        return await _apiKeyService.RotateApiKeyAsync(id, tenantId);
    }
}
