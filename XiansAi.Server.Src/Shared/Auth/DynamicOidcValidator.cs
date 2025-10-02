using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shared.Services;
using System.Security.Claims;
using System.Text.Json;

namespace Shared.Auth;

public interface IDynamicOidcValidator
{
    Task<(bool success, string? canonicalUserId, string? error)> ValidateAsync(string tenantId, string token);
}

public class DynamicOidcValidator : IDynamicOidcValidator
{
    private readonly ITenantOidcConfigService _configService;
    private readonly ILogger<DynamicOidcValidator> _logger;
    private readonly ITenantContext _tenantContext;
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _oidcManagers = new();

    public DynamicOidcValidator(
        ITenantOidcConfigService configService, 
        ILogger<DynamicOidcValidator> logger,
        ITenantContext tenantContext)
    {
        _configService = configService;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<(bool success, string? canonicalUserId, string? error)> ValidateAsync(string tenantId, string token)
    {
        try
        {
            // Minimal structural checks
            if (string.IsNullOrWhiteSpace(token) || token.Count(c => c == '.') != 2)
                return (false, null, "Invalid token format");

            // Read header payload to get iss and kid
            var handler = new JsonWebTokenHandler();
            var jwt = handler.ReadJsonWebToken(token);
            var issuer = jwt?.Issuer;
            if (string.IsNullOrWhiteSpace(issuer))
            {
                return (false, null, "Missing issuer");
            }

            var configResult = await _configService.GetForTenantAsync(tenantId);
            TenantOidcRules? rules = configResult.Data;
            if (rules == null)
            {
                return (false, null, "no auth config has set for jwt validation");
            }

            // Select provider rule based on tenant configuration
            OidcProviderRule? providerRule = null;
            string? providerName = null;
            if (rules?.Providers != null && rules.Providers.Count > 0)
            {
                IEnumerable<KeyValuePair<string, OidcProviderRule>> candidates = rules.Providers;
                if (rules.AllowedProviders != null && rules.AllowedProviders.Any())
                {
                    candidates = candidates.Where(kv => rules.AllowedProviders.Any(ap => string.Equals(ap, kv.Key, StringComparison.OrdinalIgnoreCase)));
                }

                var normIssuer = NormalizeUrl(issuer);
                foreach (var kv in candidates)
                {
                    var pr = kv.Value;
                    if ((!string.IsNullOrEmpty(pr.Issuer) && string.Equals(NormalizeUrl(pr.Issuer), normIssuer, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(pr.Authority) && (normIssuer.StartsWith(NormalizeUrl(pr.Authority), StringComparison.OrdinalIgnoreCase) || NormalizeUrl(pr.Authority).StartsWith(normIssuer, StringComparison.OrdinalIgnoreCase))))
                    {
                        providerRule = pr;
                        providerName = kv.Key;
                        break;
                    }
                }

                if (providerRule == null && rules.AllowedProviders != null && rules.AllowedProviders.Any())
                {
                    return (false, null, "Provider not allowed for tenant");
                }
            }
            else
            {
                return (false, null, "No OIDC providers configured for tenant");
            }

            if (providerRule == null)
            {
                return (false, null, "No matching OIDC provider configured for tenant");
            }

            // Discovery + configuration via OpenID Connect configuration manager
            var authority = providerRule.Authority ?? providerRule.Issuer ?? issuer;
            var metadataAddress = CombineUrl(authority, ".well-known/openid-configuration");
            var manager = GetOrCreateConfigurationManager(metadataAddress, providerRule.RequireHttpsMetadata != false);
            var oidcConfig = await manager.GetConfigurationAsync(CancellationToken.None);

            // Build validation parameters
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = providerRule.RequireSignedTokens ?? true,
                ValidateIssuer = true,
                ValidIssuer = providerRule.Issuer ?? oidcConfig.Issuer ?? issuer,
                ValidateAudience = providerRule.ExpectedAudience != null && providerRule.ExpectedAudience.Any(),
                ValidAudiences = providerRule.ExpectedAudience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = providerRule.RequireSignedTokens ?? true,
                IssuerSigningKeys = oidcConfig.SigningKeys ?? Enumerable.Empty<SecurityKey>()
            };

            // Algorithms restriction
            var allowedAlgs = providerRule.AcceptedAlgorithms ?? oidcConfig.IdTokenSigningAlgValuesSupported?.ToList();

            var tokenHandler = new JwtSecurityTokenHandler();
            tokenHandler.MapInboundClaims = false;
            tokenHandler.InboundClaimTypeMap.Clear();
            tokenHandler.OutboundClaimTypeMap.Clear();


            SecurityToken validatedToken;
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out validatedToken);

            // Check alg
            if (validatedToken is JwtSecurityToken jwtToken)
            {
                if (allowedAlgs != null && allowedAlgs.Any() && !allowedAlgs.Contains(jwtToken.Header.Alg, StringComparer.OrdinalIgnoreCase))
                {
                    return (false, null, "Signing algorithm not allowed");
                }
            }

            // Scope check if configured
            if (!string.IsNullOrWhiteSpace(providerRule.Scope))
            {
                var tokenScope = principal.Claims.FirstOrDefault(c => c.Type == "scope")?.Value ??
                principal.Claims.FirstOrDefault(c => c.Type == "scp")?.Value;
                if (string.IsNullOrWhiteSpace(tokenScope))
                {
                    return (false, null, "Missing scope claim");
                }
                var requiredScopes = providerRule.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var tokenScopes = tokenScope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                foreach (var req in requiredScopes)
                {
                    if (!tokenScopes.Contains(req))
                    {
                        return (false, null, $"Required scope missing: {req}");
                    }
                }
            }

            // Additional custom claims checks
            if (providerRule.AdditionalClaims != null)
            {
                foreach (var check in providerRule.AdditionalClaims)
                {
                    var value = principal.Claims.FirstOrDefault(c => c.Type == check.Claim)?.Value;
                    //?? principal.Claims.FirstOrDefault(c => c.Type == ClaimConstants.TenantId)?.Value
                    //?? principal.Claims.FirstOrDefault(c => c.Type == ClaimConstants.Tid)?.Value;
                    if (!EvaluateClaim(value, check))
                    {
                        return (false, null, $"Claim check failed: {check.Claim}");
                    }
                }
            }

            // Canonical user id: iss|<id>. Prefer 'sub', then 'oid', then configured/user-friendly fallbacks
            var userId = GetUserId(providerRule, principal);

            if (string.IsNullOrWhiteSpace(userId))
            {
                return (false, null, "Missing subject claim");
            }
            else if(_tenantContext.UserType != UserType.UserApiKey)
            {
                _logger.LogDebug("Setting tenant context with user ID: {userId} and user type: {userType}", userId, UserType.UserToken);
                // Set tenant context
                _tenantContext.LoggedInUser = userId;
                _tenantContext.UserType = UserType.UserToken;
                _tenantContext.Authorization = token;
                _tenantContext.TenantId = tenantId;
                _tenantContext.AuthorizedTenantIds = new[] { tenantId };
                _logger.LogDebug("User Authenticated with user ID: {userId}", userId);
            }

            var canonical = (providerName ?? issuer) + "|" + userId;
            return (true, canonical, null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return (false, null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OIDC validation");
            return (false, null, "Internal error");
        }
    }

    private static string? GetUserId(OidcProviderRule providerRule, ClaimsPrincipal principal)
    {
        string? userId = null;

        // Allow provider configuration to specify preferred claim(s)
        IEnumerable<string> configuredClaims = Enumerable.Empty<string>();
        if (providerRule.ProviderSpecificSettings != null)
        {
            if (providerRule.ProviderSpecificSettings.TryGetValue("userIdClaim", out var single))
            {
                var s = single?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) configuredClaims = new[] { s! };
            }
            else if (providerRule.ProviderSpecificSettings.TryGetValue("userIdClaims", out var list))
            {
                var s = list?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    configuredClaims = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
            }
        }

        var fallbackClaims = configuredClaims.Any()
            ? configuredClaims
            : ["sub", "oid", ClaimTypes.NameIdentifier, "preferred_username", "email", "upn", "nameid", "name" ];

        foreach (var claimType in fallbackClaims)
        {
            var val = principal.Claims.FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(val))
            {
                userId = val;
                break;
            }
        }

        return userId;
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return baseUrl + path;
    }

    private static string NormalizeUrl(string url) => url?.TrimEnd('/') ?? string.Empty;

    private static ConfigurationManager<OpenIdConnectConfiguration> GetOrCreateConfigurationManager(string metadataAddress, bool requireHttps)
    {
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

    private static bool EvaluateClaim(string? claimValue, CustomClaimCheck check)
    {
        if (claimValue == null) return false;

        // Support multi-type values (string, number, bool, arrays) coming from JSON as JsonElement
        // Normalize expected value(s) to string(s) for comparison
        var op = check.Op?.ToLowerInvariant();

        if (check.Value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var expectedValues = new List<string>();
            foreach (var item in je.EnumerateArray())
            {
                var s = JsonElementToComparableString(item);
                if (s != null) expectedValues.Add(s);
            }

            return op switch
            {
                "equals" => expectedValues.Any(v => string.Equals(claimValue, v, StringComparison.Ordinal)),
                "not_equals" => expectedValues.All(v => !string.Equals(claimValue, v, StringComparison.Ordinal)),
                "contains" => expectedValues.Any(v => claimValue.Contains(v, StringComparison.Ordinal)),
                _ => false
            };
        }

        var expected = ToComparableString(check.Value);

        return op switch
        {
            "equals" => string.Equals(claimValue, expected, StringComparison.Ordinal),
            "not_equals" => !string.Equals(claimValue, expected, StringComparison.Ordinal),
            "contains" => expected != null && claimValue.Contains(expected, StringComparison.Ordinal),
            _ => false
        };
    }

    private static string? ToComparableString(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is bool b) return b ? "true" : "false";
        if (value is JsonElement je) return JsonElementToComparableString(je);
        return value.ToString();
    }

    private static string? JsonElementToComparableString(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            // For objects/arrays, use raw text to allow exact matching if needed
            JsonValueKind.Object => je.GetRawText(),
            JsonValueKind.Array => je.GetRawText(),
            _ => je.GetRawText()
        };
    }
}

