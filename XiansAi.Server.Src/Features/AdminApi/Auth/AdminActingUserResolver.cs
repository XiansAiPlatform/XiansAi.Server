using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;

namespace Features.AdminApi.Auth;

/// <summary>
/// Outcome of attempting to resolve a verified acting human from a forwarded user token.
/// </summary>
public class AdminActingUserResolution
{
    /// <summary>True whenever a token was present at all. Distinguishes no token supplied
    /// (proceed with the API key owner's identity remains unchanged) from a token that failed validation
    /// (the whole request must be rejected).</summary>
    public bool Attempted { get; init; }
    public bool Success { get; init; }
    public string? CanonicalUserId { get; init; }
    public string[]? UserRoles { get; init; }
    public string? Error { get; init; }

    public static readonly AdminActingUserResolution NotAttempted = new() { Attempted = false };

    public static AdminActingUserResolution Failure(string error) =>
        new() { Attempted = true, Success = false, Error = error };

    public static AdminActingUserResolution Resolved(string canonicalUserId, string[] userRoles) =>
        new() { Attempted = true, Success = true, CanonicalUserId = canonicalUserId, UserRoles = userRoles };
}

public interface IAdminActingUserResolver
{
    /// <summary>
    /// Validates <paramref name="userToken"/> (the raw value of the <c>X-User-Token</c> header, or
    /// null/empty when absent) and, on success, resolves the real acting human's tenant roles.
    /// </summary>
    Task<AdminActingUserResolution> ResolveAsync(string? userToken, string finalTenantId);
}

/// <summary>
/// AdminApi is authenticated by a single shared, tenant/platform-wide API key — the key's owner,
/// not this real acting human. This resolves the optional second credential (see
/// AdminEndpointAuthenticationHandler.UserTokenHeaderName) that upgrades that assumption when the
/// caller has a real one to forward. Any AdminApi client can use this — agent-studio is today's
/// only caller, but nothing here assumes it's the only one that ever will be.
///
/// AdminApi has no per-tenant OIDC configuration for its own callers, the humans behind an
/// AdminApi client are platform staff who can operate across many real tenants, not any one
/// tenant's own end-user IdP - so the token is validated against a dedicated pseudo-tenant, the
/// same trick Features.WebApi.Auth.OidcAuthenticationHandler uses for its own static provider.
/// That pseudo-tenant's config is a named-provider list, same as any real tenant's. So a second
/// AdminApi client with its own login/IdP registers its own provider entry alongside
/// agent-studio's rather than needing a mechanism of its own (see DynamicOidcValidator.SelectProvider,
/// which already matches by issuer across however many providers are configured).
/// </summary>
public class AdminActingUserResolver : IAdminActingUserResolver
{
    public const string AdminConsolePseudoTenant = "admin-console";

    private readonly IDynamicOidcValidator _oidcValidator;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AdminActingUserResolver> _logger;

    public AdminActingUserResolver(
        IDynamicOidcValidator oidcValidator,
        IUserRepository userRepository,
        ILogger<AdminActingUserResolver> logger)
    {
        _oidcValidator = oidcValidator ?? throw new ArgumentNullException(nameof(oidcValidator));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AdminActingUserResolution> ResolveAsync(string? userToken, string finalTenantId)
    {
        if (string.IsNullOrWhiteSpace(userToken))
        {
            return AdminActingUserResolution.NotAttempted;
        }

        var validation = await _oidcValidator.ValidateAsync(AdminConsolePseudoTenant, userToken);
        if (!validation.Success || string.IsNullOrEmpty(validation.ProviderUserId))
        {
            _logger.LogWarning("Forwarded user token failed validation: {Error}", LogSanitizer.Sanitize(validation.Error));
            return AdminActingUserResolution.Failure("Invalid user token");
        }

        var canonicalUserId = validation.ProviderUserId;
        var realRoles = await _userRepository.GetUserRolesAsync(canonicalUserId, finalTenantId);

        _logger.LogInformation("Resolved verified acting user for AdminApi request: User={UserId}, Tenant={TenantId}",
            LogSanitizer.RedactUserId(canonicalUserId), LogSanitizer.Sanitize(finalTenantId));

        return AdminActingUserResolution.Resolved(canonicalUserId, realRoles.ToArray());
    }
}
