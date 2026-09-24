using Microsoft.Extensions.Caching.Memory;
using Shared.Auth;
using Shared.Exceptions;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;

namespace Features.AdminApi.Auth;

/// <summary>
/// Outcome of resolving AdminApi caller with an ID-token.
/// </summary>
public sealed record AdminKeylessResolutionResult(
    bool Success,
    string? CanonicalUserId,
    string? FinalTenantId,
    string[]? UserRoles,
    string? ErrorMessage);

/// <summary>
/// Resolves identity, tenant, and roles for an AdminApi caller who presents only an
/// <c>X-User-Token</c> OIDC ID token without an API key.
/// SysAdmin may operate on any tenant, same as <see cref="AdminRoleTenantResolver"/>'s API-key
/// path. Any other caller with at least one approved tenant membership <c>TenantUser</c>,
/// <c>TenantParticipant</c>, <c>TenantParticipantAdmin</c>, or <c>TenantAdmin</c> authenticates
/// into that tenant. What a caller may then do is entirely the capability
/// matrix's decision (<see cref="CapabilityMatrixFilter"/>).
/// </summary>
public interface IAdminKeylessUserResolver
{
    /// <summary>
    /// Validates <paramref name="userToken"/> and resolves the tenant/roles for the caller.
    /// Throws <see cref="TenantNotFoundException"/> when a SysAdmin targets a non-existent tenant,
    /// matching <see cref="AdminRoleTenantResolver"/>'s behavior so the global exception handler
    /// returns 404 the same way for both auth paths.
    /// </summary>
    /// <param name="userToken">The raw OIDC ID token from the caller's <c>X-User-Token</c> header.</param>
    /// <param name="tenantIdFromRequest">Optional tenant override from query, route, or header.</param>
    /// <param name="tenantRequiredForSysAdmin">
    /// False for a route marked <see cref="TenantOptionalForSysAdminMetadata"/> — lets a SysAdmin
    /// through without naming any tenant, since the route doesn't operate on one. True (default)
    /// keeps every tenant-scoped route's existing behavior: a SysAdmin must name an existing tenant.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminKeylessResolutionResult> ResolveAsync(
        string userToken, string? tenantIdFromRequest, bool tenantRequiredForSysAdmin = true,
        CancellationToken cancellationToken = default);
}

public sealed class AdminKeylessUserResolver : IAdminKeylessUserResolver
{
    /// <summary>
    /// Pseudo-tenant holding the OIDC provider config for AdminApi's own ID-token-only callers.
    /// Any AdminApi client may use this. each one registers its own provider entry alongside any
    /// others independently. Configured at runtime via
    /// <see cref="Endpoints.AdminConsoleOidcEndpoints"/>
    /// (<c>/api/v1/admin/admin-console/oidc-config</c>, SysAdmin-only).
    /// </summary>
    public const string AdminConsolePseudoTenant = "admin-console";

    /// <summary>
    /// Stand-in for <see cref="ITenantContext.TenantId"/> when a SysAdmin
    /// authenticates on a <see cref="TenantOptionalForSysAdminMetadata"/> route without naming a
    /// tenant.
    /// </summary>
    private const string NoTenantPlaceholder = "none";

    private readonly IDynamicOidcValidator _oidcValidator;
    private readonly IUserRepository _userRepository;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly ILogger<AdminKeylessUserResolver> _logger;

    public AdminKeylessUserResolver(
        TenantOidcConfigService tenantOidcConfigService,
        OidcValidationPolicy oidcValidationPolicy,
        IMemoryCache memoryCache,
        ILogger<DynamicOidcValidator> validatorLogger,
        IUserRepository userRepository,
        ITenantCacheService tenantCacheService,
        ILogger<AdminKeylessUserResolver> logger)
        : this(
            new DynamicOidcValidator(tenantOidcConfigService, oidcValidationPolicy, memoryCache, validatorLogger),
            userRepository, tenantCacheService, logger)
    {
    }

    /// <summary>
    /// Initializes a test instance with a preconfigured OIDC validator.
    ///
    /// This constructor is internal so dependency injection cannot select it. The shared container
    /// already registers <see cref="IDynamicOidcValidator"/> with a different
    /// <c>ITenantOidcConfigService</c> configuration than AdminApi requires. Tests can use this
    /// constructor to supply a stub validator and exercise resolver branches without contacting an
    /// identity provider.
    /// </summary>
    internal AdminKeylessUserResolver(
        IDynamicOidcValidator oidcValidator,
        IUserRepository userRepository,
        ITenantCacheService tenantCacheService,
        ILogger<AdminKeylessUserResolver> logger)
    {
        _oidcValidator = oidcValidator;
        _userRepository = userRepository;
        _tenantCacheService = tenantCacheService;
        _logger = logger;
    }

    public async Task<AdminKeylessResolutionResult> ResolveAsync(
        string userToken, string? tenantIdFromRequest, bool tenantRequiredForSysAdmin = true,
        CancellationToken cancellationToken = default)
    {
        var validation = await _oidcValidator.ValidateAsync(AdminConsolePseudoTenant, userToken);
        if (!validation.Success || string.IsNullOrEmpty(validation.ProviderUserId))
        {
            _logger.LogWarning("X-User-Token failed validation: {Error}", LogSanitizer.Sanitize(validation.Error));
            return new AdminKeylessResolutionResult(false, null, null, null, "Invalid user token");
        }

        var providerUserId = validation.ProviderUserId;

        var user = await _userRepository.GetByUserIdAsync(providerUserId);
        if (user == null && !string.IsNullOrEmpty(validation.Email) && validation.EmailVerified)
        {
            var matches = await _userRepository.GetAllByUserEmailAsync(validation.Email);
            if (matches.Count > 1)
            {
                _logger.LogWarning(
                    "X-User-Token email fallback refused: {Count} accounts share this email",
                    matches.Count);
                return new AdminKeylessResolutionResult(false, null, null, null,
                    "Multiple platform accounts share this email. sign-in cannot resolve to one");
            }

            user = matches.Count == 1 ? matches[0] : null;
            if (user != null)
            {
                providerUserId = user.UserId;
            }
        }

        if (user == null)
        {
            if (!string.IsNullOrEmpty(validation.Email) && !validation.EmailVerified)
            {
                _logger.LogWarning(
                    "X-User-Token validated but the provider did not assert email_verified, so the email fallback was not attempted for {UserId}",
                    LogSanitizer.RedactUserId(providerUserId));
            }

            _logger.LogWarning("X-User-Token validated but no platform user exists for {UserId}",
                LogSanitizer.RedactUserId(providerUserId));
            return new AdminKeylessResolutionResult(false, null, null, null, "User is not registered on this platform");
        }

        var memberTenants = user.TenantRoles
            .Where(tr => tr.IsApproved && tr.Roles.Count > 0)
            .Select(tr => tr.Tenant)
            .ToList();

        if (!user.IsSysAdmin && memberTenants.Count == 0)
        {
            _logger.LogWarning("User {UserId} is not an approved member of any tenant", LogSanitizer.RedactUserId(providerUserId));
            return new AdminKeylessResolutionResult(false, null, null, null, "User is not an approved member of any tenant");
        }

        string finalTenantId;
        if (user.IsSysAdmin)
        {
            if (!string.IsNullOrEmpty(tenantIdFromRequest))
            {
                var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantIdFromRequest, cancellationToken);
                if (tenant == null)
                {
                    _logger.LogWarning("SysAdmin user {UserId} requested non-existent tenant: {TenantId}",
                        LogSanitizer.RedactUserId(providerUserId), LogSanitizer.Sanitize(tenantIdFromRequest));
                    throw new TenantNotFoundException(tenantIdFromRequest);
                }

                finalTenantId = tenantIdFromRequest;
            }
            else if (!tenantRequiredForSysAdmin)
            {
                finalTenantId = NoTenantPlaceholder;
            }
            else
            {
                return new AdminKeylessResolutionResult(false, null, null, null,
                    "SysAdmin callers using ID-token auth must specify a tenantId (query parameter, route, or X-Tenant-Id header)");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(tenantIdFromRequest))
            {
                if (!memberTenants.Contains(tenantIdFromRequest))
                {
                    _logger.LogWarning(
                        "User {UserId} requested tenantId {TenantId} they are not an approved member of",
                        LogSanitizer.RedactUserId(providerUserId), LogSanitizer.Sanitize(tenantIdFromRequest));
                    return new AdminKeylessResolutionResult(false, null, null, null,
                        "Tenant ID does not match any tenant where the user is an approved member");
                }

                finalTenantId = tenantIdFromRequest;
            }
            else if (memberTenants.Count == 1)
            {
                finalTenantId = memberTenants[0];
            }
            else
            {
                return new AdminKeylessResolutionResult(false, null, null, null,
                    "User is a member of multiple tenants; specify tenantId explicitly");
            }
        }
        
        var roles = _userRepository.GetUserRoles(user, finalTenantId);

        _logger.LogInformation("Resolved keyless AdminApi caller: User={UserId}, Tenant={TenantId}, Roles={Roles}",
            LogSanitizer.RedactUserId(providerUserId), LogSanitizer.Sanitize(finalTenantId), LogSanitizer.Sanitize(string.Join(", ", roles)));

        return new AdminKeylessResolutionResult(true, providerUserId, finalTenantId, roles.ToArray(), null);
    }
}
