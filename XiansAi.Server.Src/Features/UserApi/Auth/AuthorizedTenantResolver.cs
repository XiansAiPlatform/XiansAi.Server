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

    public static AuthorizedTenantResolution Denied() => new();

    public static AuthorizedTenantResolution Authorized(string matchedTenantId, List<string> authorizedTenantIds) =>
        new()
        {
            IsAuthorized = true,
            MatchedTenantId = matchedTenantId,
            AuthorizedTenantIds = authorizedTenantIds
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
    private readonly ILogger<AuthorizedTenantResolver> _logger;
    private readonly TimeSpan _cacheDuration;

    public AuthorizedTenantResolver(
        IUserTenantService userTenantService,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<AuthorizedTenantResolver> logger)
    {
        _userTenantService = userTenantService;
        _cache = cache;
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

        var authorizedTenantIds = await GetApprovedTenantIdsAsync(validation);

        // Tenant ids are unique case-insensitively, so match that way and carry the stored casing
        // forward — the caller's casing must not leak into the tenant context.
        var matchedTenantId = authorizedTenantIds
            .FirstOrDefault(t => string.Equals(t, requestedTenantId, StringComparison.OrdinalIgnoreCase));

        if (matchedTenantId == null)
        {
            return AuthorizedTenantResolution.Denied();
        }

        // Copied because the source list may be a shared cache entry.
        return AuthorizedTenantResolution.Authorized(matchedTenantId, authorizedTenantIds.ToList());
    }

    /// <summary>
    /// Returns the tenants the user is an approved member of, keyed on the raw provider subject
    /// rather than the canonical `provider|subject` id, because that is the form stored in the users
    /// collection (see UnifiedAuthRequirement, which provisions users from the same raw subject).
    /// </summary>
    private async Task<IReadOnlyList<string>> GetApprovedTenantIdsAsync(OidcValidationResult validation)
    {
        var providerUserId = validation.ProviderUserId;
        if (string.IsNullOrEmpty(providerUserId))
        {
            _logger.LogWarning("Token validation returned no provider subject; denying tenant access");
            return Array.Empty<string>();
        }

        var cacheKey = CacheKeyPrefix + providerUserId;
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cachedTenantIds) && cachedTenantIds != null)
        {
            return cachedTenantIds;
        }

        var result = await _userTenantService.EnsureUserAndGetApprovedTenants(
            providerUserId, validation.Email, validation.Name);

        if (!result.IsSuccess || result.Data == null)
        {
            // Not cached: a transient failure must not lock the user out for the cache duration.
            _logger.LogWarning("Could not resolve tenants for authenticated user: {Error}",
                LogSanitizer.Sanitize(result.ErrorMessage));
            return Array.Empty<string>();
        }

        IReadOnlyList<string> tenantIds = result.Data.Select(t => t.TenantId).ToArray();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_cacheDuration)
            .SetSize(1);
        _cache.Set(cacheKey, tenantIds, cacheOptions);

        return tenantIds;
    }
}
