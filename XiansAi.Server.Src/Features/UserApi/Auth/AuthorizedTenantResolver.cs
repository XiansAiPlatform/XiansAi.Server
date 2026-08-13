using Microsoft.Extensions.Caching.Memory;
using Shared.Auth;
using Shared.Services;
using Shared.Utils;

namespace Features.UserApi.Auth;

/// <summary>
/// Outcome of checking a caller-supplied tenant id against the tenants the authenticated user is
/// actually an approved member of.
/// </summary>
public class AuthorizedTenantResolution
{
    public bool IsAuthorized { get; private init; }

    /// <summary>
    /// The requested tenant id as stored against the user, which may differ in casing from the
    /// value the caller supplied. Null when the tenant is not authorized.
    /// </summary>
    public string? MatchedTenantId { get; private init; }

    /// <summary>All tenants the user is an approved member of. Empty when nothing is authorized.</summary>
    public List<string> AuthorizedTenantIds { get; private init; } = new();

    /// <summary>
    /// The account the token acts as (the provider subject). Null when the tenant is not authorized.
    /// </summary>
    public string? AccountUserId { get; private init; }

    /// <summary>
    /// The account's stored email when present. Null when unauthorized or the account has no email.
    /// </summary>
    public string? Email { get; private init; }

    public static AuthorizedTenantResolution Denied() => new();

    public static AuthorizedTenantResolution Authorized(
        string matchedTenantId,
        List<string> authorizedTenantIds,
        string accountUserId,
        string? email = null) =>
        new()
        {
            IsAuthorized = true,
            MatchedTenantId = matchedTenantId,
            AuthorizedTenantIds = authorizedTenantIds,
            AccountUserId = accountUserId,
            Email = email
        };
}

public interface IAuthorizedTenantResolver
{
    /// <summary>
    /// Provisions the user on first sign-in, then checks whether they are an approved member of
    /// <paramref name="requestedTenantId"/>.
    /// </summary>
    Task<AuthorizedTenantResolution> ResolveAsync(OidcValidationResult validation, string requestedTenantId);
}

/// <summary>
/// Resolves the tenants a token holder may act as, shared by the UserApi HTTP and WebSocket
/// authentication handlers.
///
/// A valid token proves who the caller is, not which tenant they may act as. The tenant id arrives
/// from the query string, so it has to be checked against the tenants the user is an approved
/// member of — otherwise anyone whose token validates under another tenant's OIDC rules could act
/// as that tenant. Any failure yields an unauthorized result so that authentication fails closed.
///
/// The approved-tenant list is cached briefly per user because this runs on every JWT-authenticated
/// request and WebSocket handshake. The WebApi path already caches the equivalent lookup for five
/// minutes (Auth:TokenValidationCacheDurationMinutes), so the default here is deliberately shorter.
/// </summary>
public class AuthorizedTenantResolver : IAuthorizedTenantResolver
{
    private const string CacheKeyPrefix = "userapi_approved_tenants:";

    private readonly IUserTenantService _userTenantService;
    private readonly IMemoryCache _cache;
    private readonly OidcValidationPolicy _policy;
    private readonly ILogger<AuthorizedTenantResolver> _logger;
    private readonly TimeSpan _cacheDuration;

    public AuthorizedTenantResolver(
        IUserTenantService userTenantService,
        IMemoryCache cache,
        IConfiguration configuration,
        OidcValidationPolicy policy,
        ILogger<AuthorizedTenantResolver> logger)
    {
        _userTenantService = userTenantService;
        _cache = cache;
        _policy = policy;
        _logger = logger;
        _cacheDuration = TimeSpan.FromSeconds(
            configuration.GetValue<double>("Auth:ApprovedTenantCacheDurationSeconds", 30));
    }

    public async Task<AuthorizedTenantResolution> ResolveAsync(OidcValidationResult validation, string requestedTenantId)
    {
        if (string.IsNullOrEmpty(requestedTenantId))
        {
            _logger.LogWarning("No tenant id supplied; denying tenant access");
            return AuthorizedTenantResolution.Denied();
        }

        var access = await GetApprovedAccessAsync(validation, requestedTenantId);
        if (access == null)
        {
            return AuthorizedTenantResolution.Denied();
        }

        // Tenant ids are unique case-insensitively, so match that way and carry the stored casing
        // forward — the caller's casing must not leak into the tenant context.
        var matchedTenantId = access.TenantIds
            .FirstOrDefault(t => string.Equals(t, requestedTenantId, StringComparison.OrdinalIgnoreCase));

        if (matchedTenantId == null)
        {
            _logger.LogWarning(
                "Authenticated user {UserId} is not an approved member of tenant {TenantId}. Approved tenants: [{Tenants}]",
                LogSanitizer.Sanitize(access.AccountUserId),
                LogSanitizer.Sanitize(requestedTenantId),
                LogSanitizer.Sanitize(string.Join(", ", access.TenantIds)));
            return AuthorizedTenantResolution.Denied();
        }

        // Copied because the source list may be a shared cache entry.
        return AuthorizedTenantResolution.Authorized(
            matchedTenantId, access.TenantIds.ToList(), access.AccountUserId, access.Email);
    }

    /// <summary>The account a token resolves to and the tenants it is approved for.</summary>
    private sealed class ApprovedAccess
    {
        public required string AccountUserId { get; init; }
        public required IReadOnlyList<string> TenantIds { get; init; }
        public string? Email { get; init; }
    }

    /// <summary>
    /// Returns the account the token acts as and the tenants it is an approved member of, looked up
    /// from the raw provider subject rather than the canonical `provider|subject` id, because that is
    /// the form stored in the users collection (see UnifiedAuthRequirement, which provisions users
    /// from the same raw subject). The authority that authenticated the subject is passed through so
    /// the user record can be pinned to one provider.
    ///
    /// Null when nothing could be resolved, which denies the request.
    /// </summary>
    private async Task<ApprovedAccess?> GetApprovedAccessAsync(
        OidcValidationResult validation,
        string requestedTenantId)
    {
        var providerUserId = validation.ProviderUserId;
        if (string.IsNullOrEmpty(providerUserId))
        {
            _logger.LogWarning("Token validation returned no provider subject; denying tenant access");
            return null;
        }

        var providerAuthority = validation.ProviderAuthority;
        if (string.IsNullOrEmpty(providerAuthority))
        {
            _logger.LogWarning("Token validation returned no provider authority; denying tenant access");
            return null;
        }

        // Keyed on the provider as well as the subject: a subject is only unique within one issuer,
        // so caching on the subject alone would let another provider's identical subject read this
        // entry and skip the provider check that produced it.
        var cacheKey = CacheKeyPrefix + providerAuthority + "|" + providerUserId;
        if (_cache.TryGetValue(cacheKey, out ApprovedAccess? cachedAccess) && cachedAccess != null)
        {
            return cachedAccess;
        }

        // The address is recorded whether or not the provider vouched for it, because a record with
        // no address cannot be matched to a person at all. Whether it may decide *identity* is a
        // separate question the stored EmailVerified flag answers: an unverified claim is only
        // display and contact, or someone able to assert an arbitrary email at their IdP could claim
        // a victim's address. See OidcTokenInspector.IsEmailVerified.
        //
        // The membership is created approved when the token was checked against the audiences this
        // tenant declared, because that is what makes holding one the tenant's own statement that
        // this person belongs to it — unlike the WebAPI console, which validates against the
        // deployment-wide provider and therefore still requires an admin to approve.
        //
        // A provider that declares no audience accepts anything its issuer signed, including a
        // token minted for an unrelated application there, so holding one says nothing about this
        // tenant. Those fall back to a pending membership for an admin to approve, which is where
        // they sat before automatic approval existed. Declaring an audience is what earns it.
        if (!validation.AudienceValidated)
        {
            _policy.WarnAboutConfiguration("approval:" + requestedTenantId,
                "Not auto-approving membership of tenant {TenantId}: its OIDC provider declares no " +
                "expectedAudience, so this token was accepted on its issuer's signature alone. New " +
                "members wait for an admin until expectedAudience is set on the provider.",
                LogSanitizer.Sanitize(requestedTenantId));
        }

        var result = await _userTenantService.EnsureUserAndGetApprovedTenants(
            new SignInIdentity
            {
                UserId = providerUserId,
                Email = validation.Email,
                EmailVerified = validation.EmailVerified,
                Name = validation.Name,
                ProviderAuthority = providerAuthority
            },
            requestedTenantId,
            approveNewMembership: validation.AudienceValidated);

        if (!result.IsSuccess || result.Data == null)
        {
            // Not cached: a transient failure must not lock the user out for the cache duration.
            _logger.LogWarning("Could not resolve tenants for authenticated user: {Error}",
                LogSanitizer.Sanitize(result.ErrorMessage));
            return null;
        }

        var access = new ApprovedAccess
        {
            AccountUserId = result.Data.UserId,
            TenantIds = result.Data.Tenants.Select(t => t.TenantId).ToArray(),
            Email = result.Data.Email
        };

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheDuration)
            .SetSize(1);
        _cache.Set(cacheKey, access, cacheOptions);

        return access;
    }
}
