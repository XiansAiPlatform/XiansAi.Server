using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Shared.Providers.Auth.Oidc;

public class OidcTokenService : ITokenService
{
    private readonly ILogger<OidcTokenService> _logger;
    private readonly OidcConfig _config;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly IMemoryCache _cache;

    public OidcTokenService(
        ILogger<OidcTokenService> logger,
        IConfiguration configuration,
        IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;

        // Load OIDC configuration
        _config = configuration.GetSection("Oidc").Get<OidcConfig>() ??
            throw new ArgumentException("OIDC configuration is missing");

        if (string.IsNullOrEmpty(_config.Authority))
            throw new ArgumentException("OIDC Authority is missing");

        if (string.IsNullOrEmpty(_config.Audience))
            throw new ArgumentException("OIDC Audience is missing");

        // Initialize configuration manager for fetching OIDC metadata
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{_config.Authority.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever());
    }

    public async Task<(bool success, string? userId)> ProcessToken(string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Token is null or empty");
                return (false, null);
            }
            
            // Log token format for debugging (first/last few chars only for security)
            _logger.LogDebug("Processing token: {TokenStart}...{TokenEnd} (length: {Length})", 
                token.Substring(0, Math.Min(10, token.Length)), 
                token.Length > 10 ? token.Substring(token.Length - 10) : "", 
                token.Length);
            
            // Check cache first
            var cacheKey = GetCacheKey(token);
            if (_cache.TryGetValue<(bool success, string? userId)>(cacheKey, out var cachedResult))
            {
                _logger.LogDebug("Token validation cache hit");
                return cachedResult;
            }

            var handler = new JwtSecurityTokenHandler();
            
            // Validate JWT format before processing (must have 3 segments: header.payload.signature)
            var segments = token.Split('.');
            if (segments.Length != 3)
            {
                _logger.LogError("Invalid JWT format: token has {SegmentCount} segments, expected 3. Token: {TokenPreview}...", 
                    segments.Length, 
                    token.Length > 50 ? token.Substring(0, 50) : token);
                return (false, null);
            }
            
            // Get OIDC configuration from discovery endpoint
            var oidcConfig = await _configurationManager!.GetConfigurationAsync(CancellationToken.None);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _config.Issuer ?? _config.Authority,
                ValidateAudience = true,
                ValidAudience = _config.Audience,
                ValidateIssuerSigningKey = _config.RequireSignedTokens,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                RequireSignedTokens = _config.RequireSignedTokens,
                ValidAlgorithms = _config.AcceptedAlgorithms
            };

            _logger.LogDebug("Validating JWT token with {KeyCount} signing keys", oidcConfig.SigningKeys.Count);
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            
            // Extract user ID using configured claim mapping
            var userId = principal.FindFirst(_config.ClaimMappings.UserIdClaim)?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("User ID claim '{UserIdClaim}' not found in token", _config.ClaimMappings.UserIdClaim);
                var result = (false, (string?)null);
                // Don't cache failed validations
                return result;
            }

            _logger.LogInformation("Successfully validated OIDC token for user: {UserId}", userId);
            var successResult = (true, (string?)userId);
            
            // Cache successful validation for 5 minutes
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                .SetPriority(CacheItemPriority.Normal)
                .SetSize(1);
            _cache.Set(cacheKey, successResult, cacheOptions);
            
            return successResult;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning("Token expired: {Message}", ex.Message);
            return (false, null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogError("Token validation failed: {Message}", ex.Message);
            return (false, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token validation");
            return (false, null);
        }
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        // Try the configured user ID claim first (allows per-provider customization)
        var userId = token.Claims.FirstOrDefault(c => c.Type == _config.ClaimMappings.UserIdClaim)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Found user ID in '{ClaimType}' claim: {UserId}", _config.ClaimMappings.UserIdClaim, userId);
            return userId;
        }

        // Prefer 'preferred_username' over 'sub' for readable user IDs
        // 'preferred_username' is a standard OIDC claim (RFC) and provides human-readable identifiers
        // This aligns with database design where user_id is stored as readable strings (e.g., "admin")
        userId = token.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Found user ID in 'preferred_username' claim: {UserId}", userId);
            return userId;
        }

        // Fallback to standard 'sub' claim (UUID/immutable identifier)
        // 'sub' is always present in OIDC tokens and is immutable, making it reliable for providers
        // that don't include 'preferred_username' or when it's not available
        userId = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Found user ID in 'sub' claim: {UserId}", userId);
            return userId;
        }

        // Try other common alternative claim names as last resort
        var alternativeClaimTypes = new[] { "user_id", "uid", "id", "email", "username" };
        foreach (var claimType in alternativeClaimTypes)
        {
            userId = token.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("Found user ID in '{ClaimType}' claim: {UserId}", claimType, userId);
                return userId;
            }
        }

        _logger.LogWarning("No user identifier found in any known claim types");
        return null;
    }

    public IEnumerable<string> ExtractTenantIds(JwtSecurityToken token)
    {
        // Generic OIDC providers don't have a standard tenant claim
        // This can be customized based on the specific provider's token structure
        _logger.LogDebug("Generic OIDC provider does not have standard tenant claims");
        return new List<string>();
    }

    private string GetCacheKey(string token)
    {
        // Use SHA256 hash to avoid collisions and not log the full token
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        var hash = Convert.ToBase64String(hashBytes);
        return $"oidc_token_validation:{hash}";
    }

    public Task<string> GetManagementApiToken()
    {
        throw new NotImplementedException("Management API not supported for generic OIDC provider");
    }
}

