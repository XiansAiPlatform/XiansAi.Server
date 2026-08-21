using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shared.Data.Models.Validation;
using Shared.Services;
using Shared.Utils;

namespace Shared.Auth;

/// <summary>
/// Validates JWTs against the OIDC rules a tenant has configured.
///
/// This class only establishes who the caller is. It deliberately has no side effects on the
/// request's tenant context: a token proves an identity, not an authorization, and the two were
/// previously entangled here. Callers decide what the validated identity means for their transport.
///
/// The rules are per-tenant records edited at runtime rather than reviewed deployment
/// configuration, so <see cref="OidcValidationPolicy"/> gets the final say on anything that would
/// weaken validation.
/// </summary>
public class DynamicOidcValidator : IDynamicOidcValidator
{
    private const string ValidationCacheKeyPrefix = "oidc_validation:";

    /// <summary>
    /// Caps how many distinct providers we will hold discovery state for. Authorities come from
    /// tenant records, so without a ceiling this static map grows with whatever gets configured.
    /// </summary>
    private const int MaxConfigurationManagers = 500;

    /// <summary>Reusable and thread-safe; the handler holds no per-request state.</summary>
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _oidcManagers = new();

    private readonly ITenantOidcConfigService _configService;
    private readonly OidcValidationPolicy _policy;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DynamicOidcValidator> _logger;

    public DynamicOidcValidator(
        ITenantOidcConfigService configService,
        OidcValidationPolicy policy,
        IMemoryCache cache,
        ILogger<DynamicOidcValidator> logger)
    {
        _configService = configService;
        _policy = policy;
        _cache = cache;
        _logger = logger;
    }

    public async Task<OidcValidationResult> ValidateAsync(string tenantId, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || CountDots(token) != 2)
        {
            return OidcValidationResult.Fail("Invalid token format");
        }

        // Validated before it reaches the configuration lookup, which is keyed and cached on this
        // value: without the check, a caller could mint an unbounded number of distinct cache
        // entries just by varying the parameter.
        if (!ValidationHelpers.IsValidPattern(tenantId, ValidationHelpers.Patterns.SafeTenantId))
        {
            _logger.LogWarning("Rejecting malformed tenant id in token validation request");
            return OidcValidationResult.Fail("Invalid tenant");
        }

        var cached = TryGetCachedValidation(tenantId, token);
        if (cached != null)
        {
            return cached;
        }

        var result = await ValidateUncachedAsync(tenantId, token);
        if (result.Success)
        {
            CacheValidation(tenantId, token, result);
        }

        return result;
    }

    private async Task<OidcValidationResult> ValidateUncachedAsync(string tenantId, string token)
    {
        try
        {
            var jwt = TokenHandler.ReadJsonWebToken(token);
            var issuer = jwt?.Issuer;
            if (jwt == null || string.IsNullOrWhiteSpace(issuer))
            {
                return OidcValidationResult.Fail("Missing issuer");
            }

            var configResult = await _configService.GetForTenantAsync(tenantId);
            var rules = configResult.Data;
            if (rules == null)
            {
                return OidcValidationResult.Fail("No auth config has been set for jwt validation");
            }

            if (rules.Providers == null || rules.Providers.Count == 0)
            {
                return OidcValidationResult.Fail("No OIDC providers configured for tenant");
            }

            var (providerName, providerRule) = SelectProvider(rules, issuer);
            if (providerRule == null)
            {
                return OidcValidationResult.Fail(rules.AllowedProviders is { Count: > 0 }
                    ? "Provider not allowed for tenant"
                    : "No matching OIDC provider configured for tenant");
            }

            var providerLabel = tenantId + "/" + providerName;
            var authority = providerRule.Authority ?? providerRule.Issuer ?? issuer;

            var unsafeAuthority = _policy.DescribeUnsafeAuthority(authority);
            if (unsafeAuthority != null)
            {
                _logger.LogError("Refusing OIDC discovery for {Provider}: {Reason}",
                    LogSanitizer.Sanitize(providerLabel), LogSanitizer.Sanitize(unsafeAuthority));
                return OidcValidationResult.Fail("Provider authority is not permitted");
            }

            var oidcConfig = await GetOpenIdConfigurationAsync(authority, providerLabel);
            if (oidcConfig == null)
            {
                return OidcValidationResult.Fail("Provider metadata is unavailable");
            }

            var parameters = BuildValidationParameters(providerRule, providerLabel, oidcConfig, issuer);
            if (parameters == null)
            {
                return OidcValidationResult.Fail("Provider configuration is not permitted");
            }

            var validation = await TokenHandler.ValidateTokenAsync(jwt, parameters);
            if (!validation.IsValid)
            {
                _logger.LogWarning(validation.Exception, "Token validation failed for {Provider}",
                    LogSanitizer.Sanitize(providerLabel));
                return OidcValidationResult.Fail(validation.Exception?.Message ?? "Token validation failed");
            }

            var ruleFailure = OidcTokenInspector.DescribeMissingScope(providerRule, jwt)
                ?? OidcTokenInspector.DescribeFailedClaimCheck(providerRule, jwt);
            if (ruleFailure != null)
            {
                return OidcValidationResult.Fail(ruleFailure);
            }

            var subject = ResolveSubject(providerRule, providerLabel, jwt);
            if (string.IsNullOrWhiteSpace(subject.Value))
            {
                return OidcValidationResult.Fail("Missing subject claim");
            }

            var canonical = (providerName ?? issuer) + "|" + subject.Value;
            return OidcValidationResult.Ok(
                canonical,
                subject.Value,
                NormalizeUrl(authority),
                OidcTokenInspector.GetEmail(jwt),
                OidcTokenInspector.GetName(jwt),
                OidcTokenInspector.ExpiresAt(jwt),
                // The parameters record whether the provider declared any audience to check
                // against, so callers can tell an audience-checked token from one accepted on its
                // issuer's signature alone.
                parameters.ValidateAudience);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return OidcValidationResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OIDC validation");
            return OidcValidationResult.Fail("Internal error");
        }
    }

    /// <summary>
    /// Builds the validation rules, overriding anything in the tenant's configuration that would
    /// weaken them. Returns null when the configuration cannot be made safe at all.
    /// </summary>
    private TokenValidationParameters? BuildValidationParameters(
        OidcProviderRule providerRule,
        string providerLabel,
        OpenIdConnectConfiguration oidcConfig,
        string issuer)
    {
        // Signature verification is not negotiable. A provider that turns it off is accepting
        // tokens anyone can mint, so the setting is overridden rather than honoured.
        if (providerRule.RequireSignedTokens == false)
        {
            _policy.WarnAboutConfiguration("signing:" + providerLabel,
                "OIDC provider {Provider} sets requireSignedTokens=false. Ignoring it: tokens are " +
                "always signature-verified. Remove the setting from the tenant configuration.",
                LogSanitizer.Sanitize(providerLabel));
        }

        var hasAudience = providerRule.ExpectedAudience is { Count: > 0 };
        if (!hasAudience)
        {
            if (_policy.RequireAudience)
            {
                _logger.LogError("OIDC provider {Provider} declares no expectedAudience and audience " +
                    "enforcement is on, so no token from it can be accepted.",
                    LogSanitizer.Sanitize(providerLabel));
                return null;
            }

            _policy.WarnAboutConfiguration("audience:" + providerLabel,
                "OIDC provider {Provider} declares no expectedAudience, so any token this issuer " +
                "signed is accepted — including tokens minted for a different application. Set " +
                "expectedAudience, then enable Auth:RequireOidcAudience.",
                LogSanitizer.Sanitize(providerLabel));
        }

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuer = providerRule.Issuer ?? oidcConfig.Issuer ?? issuer,
            ValidateAudience = hasAudience,
            ValidAudiences = providerRule.ExpectedAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,

            // Restricting algorithms here rather than checking after the fact means an algorithm
            // the provider does not accept never gets as far as a signature comparison.
            ValidAlgorithms = OidcValidationPolicy.ResolveAllowedAlgorithms(
                providerRule.AcceptedAlgorithms, oidcConfig.IdTokenSigningAlgValuesSupported),
            IssuerSigningKeys = oidcConfig.SigningKeys ?? Enumerable.Empty<SecurityKey>()
        };
    }

    /// <summary>
    /// Reads the subject, warning when it came from a claim the user may be able to change at their
    /// identity provider — which would let them resolve to somebody else's record.
    ///
    /// A configured claim always reports <see cref="ResolvedSubject.IsStableClaim"/> as true
    /// (the nomination is deliberate), so a mutable <c>userIdClaim</c> would otherwise be silent.
    /// That case gets its own throttled warning under a distinct key.
    /// </summary>
    private ResolvedSubject ResolveSubject(OidcProviderRule providerRule, string providerLabel, JsonWebToken jwt)
    {
        var subject = OidcTokenInspector.ResolveSubject(
            providerRule, jwt, allowFallbackClaims: !_policy.RequireStandardSubjectClaim);

        if (!subject.IsStableClaim && subject.Value != null)
        {
            _policy.WarnAboutConfiguration("subject:" + providerLabel + ":" + subject.ClaimType,
                "OIDC provider {Provider} has no 'sub' or 'oid' claim, so identity fell back to " +
                "'{ClaimType}', which users can often change at their provider. Set userIdClaim on " +
                "the provider to name a stable claim, then enable Auth:StrictSubjectClaim.",
                LogSanitizer.Sanitize(providerLabel), LogSanitizer.Sanitize(subject.ClaimType));
        }
        else if (subject.IsStableClaim && subject.ClaimType != null)
        {
            // Configured claims are treated as deliberate by ResolveSubject, so a mutable
            // userIdClaim would otherwise never surface. Tenants already grandfathered past the
            // upsert refusal still need to be visible in the log.
            var mutableReason = OidcTokenInspector.DescribeMutableSubjectClaim(subject.ClaimType);
            if (mutableReason != null)
            {
                _policy.WarnAboutConfiguration(
                    "mutable-useridclaim:" + providerLabel + ":" + subject.ClaimType,
                    "OIDC provider {Provider} nominates mutable userIdClaim '{ClaimType}'. {Reason}",
                    LogSanitizer.Sanitize(providerLabel),
                    LogSanitizer.Sanitize(subject.ClaimType),
                    mutableReason);
            }
        }

        return subject;
    }

    /// <summary>
    /// Picks the configured provider whose issuer or authority the token's issuer corresponds to.
    /// When the tenant restricts itself to a subset of providers, only that subset is considered.
    /// </summary>
    private static (string? Name, OidcProviderRule? Rule) SelectProvider(TenantOidcRules rules, string issuer)
    {
        var normalizedIssuer = NormalizeUrl(issuer);
        var allowedProviders = rules.AllowedProviders;
        var restricted = allowedProviders is { Count: > 0 };

        foreach (var (name, rule) in rules.Providers!)
        {
            if (restricted &&
                !allowedProviders!.Any(allowed => string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (MatchesIssuer(rule, normalizedIssuer))
            {
                return (name, rule);
            }
        }

        return (null, null);
    }

    private static bool MatchesIssuer(OidcProviderRule rule, string normalizedIssuer)
    {
        if (!string.IsNullOrEmpty(rule.Issuer) &&
            string.Equals(NormalizeUrl(rule.Issuer), normalizedIssuer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrEmpty(rule.Authority))
        {
            return false;
        }

        var normalizedAuthority = NormalizeUrl(rule.Authority);
        return normalizedIssuer.StartsWith(normalizedAuthority, StringComparison.OrdinalIgnoreCase) ||
               normalizedAuthority.StartsWith(normalizedIssuer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches the provider's discovery document, under a timeout so that an unresponsive identity
    /// provider cannot hold an authentication request open for the HTTP client's default timeout.
    /// </summary>
    private async Task<OpenIdConnectConfiguration?> GetOpenIdConfigurationAsync(string authority, string providerLabel)
    {
        var metadataAddress = CombineUrl(authority, ".well-known/openid-configuration");

        var manager = GetOrCreateConfigurationManager(metadataAddress, !_policy.AllowInsecureAuthority);
        if (manager == null)
        {
            _logger.LogError("Refusing OIDC discovery for {Provider}: tracking more than {Limit} providers",
                LogSanitizer.Sanitize(providerLabel), MaxConfigurationManagers);
            return null;
        }

        try
        {
            using var timeout = new CancellationTokenSource(_policy.DiscoveryTimeout);
            return await manager.GetConfigurationAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not fetch OIDC metadata for {Provider}", LogSanitizer.Sanitize(providerLabel));
            return null;
        }
    }

    private OidcValidationResult? TryGetCachedValidation(string tenantId, string token)
    {
        if (_policy.ValidationCacheDuration <= TimeSpan.Zero)
        {
            return null;
        }

        return _cache.TryGetValue(BuildCacheKey(tenantId, token), out OidcValidationResult? cached)
            ? cached
            : null;
    }

    /// <summary>
    /// Caches a successful validation so that repeat requests carrying the same token — a chatty
    /// REST client, an SSE reconnect loop — do not re-verify the signature and re-read the tenant's
    /// OIDC rules every time.
    ///
    /// The entry never outlives the token: an expiry beyond the token's own would let an expired
    /// token keep working, which is the one thing caching here must not do.
    /// </summary>
    private void CacheValidation(string tenantId, string token, OidcValidationResult result)
    {
        if (_policy.ValidationCacheDuration <= TimeSpan.Zero)
        {
            return;
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(_policy.ValidationCacheDuration);
        if (result.TokenExpiresAt is { } tokenExpiry && tokenExpiry < expiresAt)
        {
            expiresAt = tokenExpiry;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return;
        }

        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(expiresAt)
            .SetSize(1);
        _cache.Set(BuildCacheKey(tenantId, token), result, options);
    }

    /// <summary>
    /// Keyed on the tenant as well as the token, because the same token validates against different
    /// rules per tenant and may legitimately be accepted by one and refused by another. The token
    /// is hashed so that a credential never appears in a cache key.
    /// </summary>
    private static string BuildCacheKey(string tenantId, string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return ValidationCacheKeyPrefix + tenantId + ":" + Convert.ToBase64String(hash);
    }

    /// <summary>A compact JWS has exactly two dots. Counted directly to avoid a LINQ pass per request.</summary>
    private static int CountDots(string token)
    {
        var dots = 0;
        foreach (var c in token)
        {
            if (c == '.')
            {
                dots++;
            }
        }

        return dots;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return baseUrl + path;
    }

    private static string NormalizeUrl(string url) => url?.TrimEnd('/') ?? string.Empty;

    /// <summary>
    /// Returns the shared discovery client for an address, or null once the cap is reached.
    ///
    /// <paramref name="requireHttps"/> restates the scheme rule the authority was already checked
    /// against, so that a redirect cannot walk the fetch down to plaintext. It comes from the
    /// deployment's environment and so is the same for every address here.
    /// </summary>
    private static ConfigurationManager<OpenIdConnectConfiguration>? GetOrCreateConfigurationManager(
        string metadataAddress,
        bool requireHttps)
    {
        if (_oidcManagers.TryGetValue(metadataAddress, out var existing))
        {
            return existing;
        }

        if (_oidcManagers.Count >= MaxConfigurationManagers)
        {
            return null;
        }

        return _oidcManagers.GetOrAdd(metadataAddress, address =>
        {
            var retriever = new HttpDocumentRetriever { RequireHttps = requireHttps };
            return new ConfigurationManager<OpenIdConnectConfiguration>(address, new OpenIdConnectConfigurationRetriever(), retriever)
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(12),
                RefreshInterval = TimeSpan.FromMinutes(5)
            };
        });
    }
}
