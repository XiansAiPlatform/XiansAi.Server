using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Services;

/// <summary>
/// Response returned by the platform bootstrap operation.
/// Contains the freshly minted API key that the caller should use to configure AgentStudio.
/// </summary>
public class BootstrapResponse
{
    /// <summary>
    /// The raw API key (sk-Xnai-...). This is the only time the key is returned in clear text.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The tenant the SysAdmin user and API key belong to.
    /// </summary>
    public required string TenantId { get; set; }

    /// <summary>
    /// The user id (same as the supplied email) of the created SysAdmin.
    /// </summary>
    public required string UserId { get; set; }
}

public interface IBootstrapService
{
    /// <summary>
    /// Bootstraps the platform when no users exist yet: creates a SysAdmin user from the
    /// supplied email, ensures an enabled tenant exists, mints an API key, and returns it.
    /// </summary>
    /// <param name="email">Email used as both the user id and email of the SysAdmin.</param>
    /// <param name="tenantId">Optional tenant id. Defaults to the platform default tenant.</param>
    Task<ServiceResult<BootstrapResponse>> BootstrapAsync(string email, string? tenantId);
}

/// <summary>
/// Orchestrates a one-time platform bootstrap. Protected solely by the "no users exist" gate:
/// once any user exists, the operation is rejected with a conflict.
/// </summary>
public class BootstrapService : IBootstrapService
{
    private const string BootstrapApiKeyName = "bootstrap-admin-key";
    private const string SystemCreator = "system";

    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserManagementService _userManagementService;
    private readonly IApiKeyService _apiKeyService;
    private readonly ILogger<BootstrapService> _logger;

    public BootstrapService(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        IUserManagementService userManagementService,
        IApiKeyService apiKeyService,
        ILogger<BootstrapService> logger)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _apiKeyService = apiKeyService ?? throw new ArgumentNullException(nameof(apiKeyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<BootstrapResponse>> BootstrapAsync(string email, string? tenantId)
    {
        try
        {
            // 1. Validate email
            email = email?.Trim() ?? string.Empty;
            if (!IsValidEmail(email))
            {
                return ServiceResult<BootstrapResponse>.BadRequest("A valid 'email' query parameter is required");
            }

            // 2. Gate: only allowed while no users exist
            var existingUser = await _userRepository.GetAnyUserAsync();
            if (existingUser != null)
            {
                _logger.LogWarning("Bootstrap attempt rejected: platform already has users");
                return ServiceResult<BootstrapResponse>.Conflict("Platform already bootstrapped");
            }

            // 3. Resolve tenant id
            var resolvedTenantId = string.IsNullOrWhiteSpace(tenantId) ? global::Shared.Utils.Constants.DefaultTenantId : tenantId.Trim();

            // 4. Ensure an enabled tenant exists
            var tenantResult = await EnsureEnabledTenantAsync(resolvedTenantId, email);
            if (!tenantResult.IsSuccess)
            {
                return ServiceResult<BootstrapResponse>.Failure(
                    tenantResult.ErrorMessage ?? "Failed to ensure tenant", tenantResult.StatusCode);
            }

            // 5. Create the SysAdmin user (CreateNewUser sets IsSysAdmin = true because no users exist)
            var createUserResult = await _userManagementService.CreateNewUser(new UserDto
            {
                UserId = email,
                Email = email,
                Name = email
            });
            if (!createUserResult.IsSuccess)
            {
                return ServiceResult<BootstrapResponse>.Failure(
                    createUserResult.ErrorMessage ?? "Failed to create bootstrap user", createUserResult.StatusCode);
            }

            // 6. Purge any pre-existing API keys across all tenants. The "no users" gate guarantees
            // these are orphaned (their owners are gone), and removing them avoids both a stale-credential
            // security gap and a duplicate-name conflict on the fixed bootstrap key name.
            var purgeResult = await _apiKeyService.DeleteAllApiKeysAsync();
            if (!purgeResult.IsSuccess)
            {
                return ServiceResult<BootstrapResponse>.Failure(
                    purgeResult.ErrorMessage ?? "Failed to clear existing API keys", purgeResult.StatusCode);
            }
            if (purgeResult.Data > 0)
            {
                _logger.LogWarning("Bootstrap removed {Count} pre-existing API key(s) before minting the bootstrap key",
                    purgeResult.Data);
            }

            // 7. Mint an API key owned by the new SysAdmin
            var apiKeyResult = await _apiKeyService.CreateApiKeyAsync(resolvedTenantId, BootstrapApiKeyName, email);
            if (!apiKeyResult.IsSuccess)
            {
                // The platform is still bootstrapped (SysAdmin exists); the admin can mint a key from the UI.
                _logger.LogError("Bootstrap created SysAdmin {Email} but failed to mint API key: {Error}",
                    LogSanitizer.Sanitize(email), LogSanitizer.Sanitize(apiKeyResult.ErrorMessage));
                return ServiceResult<BootstrapResponse>.Failure(
                    apiKeyResult.ErrorMessage ?? "Failed to create API key", apiKeyResult.StatusCode);
            }

            _logger.LogInformation("Platform bootstrapped with SysAdmin {Email} on tenant {TenantId}",
                LogSanitizer.Sanitize(email), LogSanitizer.Sanitize(resolvedTenantId));

            return ServiceResult<BootstrapResponse>.Success(new BootstrapResponse
            {
                ApiKey = apiKeyResult.Data.apiKey,
                TenantId = resolvedTenantId,
                UserId = email
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during platform bootstrap");
            return ServiceResult<BootstrapResponse>.InternalServerError("An error occurred while bootstrapping the platform");
        }
    }

    /// <summary>
    /// Ensures a tenant with the given id exists and is enabled, creating it if necessary.
    /// New tenants are created directly as enabled (unlike TenantService.CreateTenant which disables them)
    /// so the SysAdmin can see and use the tenant immediately.
    /// </summary>
    private async Task<ServiceResult<Tenant>> EnsureEnabledTenantAsync(string tenantId, string createdBy)
    {
        try
        {
            string sanitizedTenantId;
            try
            {
                sanitizedTenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            }
            catch (ValidationException ex)
            {
                return ServiceResult<Tenant>.BadRequest(ex.Message);
            }

            var existingTenant = await _tenantRepository.GetByTenantIdAsync(sanitizedTenantId);
            if (existingTenant != null)
            {
                if (!existingTenant.Enabled)
                {
                    existingTenant.Enabled = true;
                    await _tenantRepository.UpdateAsync(existingTenant.Id, existingTenant);
                }
                return ServiceResult<Tenant>.Success(existingTenant);
            }

            var tenant = new Tenant
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = sanitizedTenantId,
                Name = sanitizedTenantId,
                Enabled = true,
                CreatedBy = SystemCreator,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var validatedTenant = tenant.SanitizeAndValidate();
            await _tenantRepository.CreateAsync(validatedTenant);
            return ServiceResult<Tenant>.Success(validatedTenant);
        }
        catch (ValidationException ex)
        {
            return ServiceResult<Tenant>.BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring tenant {TenantId} during bootstrap", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while ensuring the tenant");
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }
        return new EmailAddressAttribute().IsValid(email);
    }
}
