using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RestSharp;
using Shared.Utils;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;

namespace Shared.Providers.Auth.Keycloak;

public class KeycloakTokenService : ITokenService
{
    private readonly ILogger<KeycloakTokenService> _logger;
    private RestClient _client;
    private readonly KeycloakConfig? _keycloakConfig;
    private readonly string _organizationClaimType = "organization";
    private readonly HttpClient _httpClient;
    private static readonly Dictionary<string, (DateTime expiry, JsonWebKeySet jwks)> _jwksCache = new();
    private static readonly SemaphoreSlim _jwksCacheLock = new(1, 1);

    public KeycloakTokenService(ILogger<KeycloakTokenService> logger, IConfiguration configuration, HttpClient? httpClient = null)
    {
        _logger = logger;
        _client = new RestClient();
        _httpClient = httpClient ?? new HttpClient();
        _keycloakConfig = configuration.GetSection("Keycloak").Get<KeycloakConfig>() ??
            throw new ArgumentException("Keycloak configuration is missing");
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        // First try the standard 'sub' claim
        var userId = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Found user ID in 'sub' claim: {UserId}", userId);
            return userId;
        }

        // Fallback to preferred_username for Keycloak tokens missing 'sub' claim
        userId = token.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogInformation("Using 'preferred_username' as user ID: {UserId}", userId);
            return userId;
        }

        // Try other common alternative claim names
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
        try
        {
            var defaultTenantId = Constants.DefaultTenantId;
            // Find the organization claim
            var organizationClaim = token.Claims.FirstOrDefault(c => c.Type == _organizationClaimType);
            if (organizationClaim == null)
            {
                _logger.LogWarning("No organization claim found in token");
                return new List<string> { defaultTenantId };
            }

            // The organization claim contains a JSON object with tenant IDs as properties
            var organizationJson = organizationClaim.Value;
            if (string.IsNullOrEmpty(organizationJson))
            {
                _logger.LogWarning("Organization claim is empty");
                return new List<string> { defaultTenantId };
            }

            // Parse the organization JSON
            var tenantIds = new List<string>();
            try
            {
                var organizationObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(organizationJson);
                if (organizationObj != null)
                {
                    // Extract tenant IDs which are the keys of the dictionary
                    tenantIds.AddRange(organizationObj.Keys);
                }
                tenantIds.Add(defaultTenantId);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse organization claim JSON: {Value}", organizationJson);
            }

            _logger.LogInformation("Extracted tenant IDs from token: {TenantIds}", string.Join(", ", tenantIds));
            return tenantIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting tenant IDs from token");
            return Enumerable.Empty<string>();
        }
    }

    public async Task<(bool success, string? userId)> ProcessToken(string token)
    {
        try
        {
            // Validate the JWT token with JWKS
            var validationResult = await ValidateJwtWithJwks(token);
            if (!validationResult.success)
            {
                _logger.LogWarning("JWT token validation failed");
                return (false, null);
            }

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return (false, null);
            }

            var userId = ExtractUserId(jsonToken);

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return (false, null);
            }

            return (true, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null);
        }
    }

    private async Task<(bool success, string? errorMessage)> ValidateJwtWithJwks(string token)
    {
        try
        {
            if (_keycloakConfig == null)
            {
                return (false, "Keycloak configuration is missing");
            }

            // Get JWKS
            var jwks = await GetJwks();
            if (jwks == null)
            {
                return (false, "Failed to fetch JWKS from Keycloak");
            }

            // Parse JWT header to get key ID
            var handler = new JsonWebTokenHandler();
            var jsonToken = handler.ReadJsonWebToken(token);

            if (jsonToken == null)
            {
                return (false, "Invalid JWT token format");
            }

            var kid = jsonToken.Kid;
            if (string.IsNullOrEmpty(kid))
            {
                return (false, "JWT token missing key ID (kid)");
            }

            // Find the matching key in JWKS
            var matchingKey = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
            if (matchingKey == null)
            {
                return (false, $"No matching key found in JWKS for kid: {kid}");
            }

            // Create RSA security key from JWKS
            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Base64UrlEncoder.DecodeBytes(matchingKey.N),
                Exponent = Base64UrlEncoder.DecodeBytes(matchingKey.E)
            });

            var rsaSecurityKey = new RsaSecurityKey(rsa)
            {
                KeyId = matchingKey.Kid
            };

            // Set up token validation parameters
            var issuerUri = _keycloakConfig.ValidIssuer ?? throw new InvalidOperationException("Keycloak configuration ValidIssuer is missing");
            var validationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = true,
                ValidIssuers = issuerUri.Split(','),
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = rsaSecurityKey,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            // Validate the token
            var result = await handler.ValidateTokenAsync(token, validationParameters);

            if (!result.IsValid)
            {
                var errorMessage = result.Exception?.Message ?? "Token validation failed";
                _logger.LogWarning("JWT validation failed: {ErrorMessage}", errorMessage);
                return (false, errorMessage);
            }

            _logger.LogDebug("JWT token validated successfully");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token with JWKS");
            return (false, ex.Message);
        }
    }

    private async Task<JsonWebKeySet?> GetJwks()
    {
        var baseUri = new Uri(_keycloakConfig?.AuthServerUrl!);
        var realmUri = new Uri(baseUri, $"realms/{_keycloakConfig?.Realm}");
        var cacheKey = realmUri.ToString();

        await _jwksCacheLock.WaitAsync();
        try
        {
            // Check cache first
            if (_jwksCache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.UtcNow)
            {
                return cached.jwks;
            }

            // Fetch fresh JWKS
            var realmUriWithSlash = new Uri(realmUri.ToString() + "/");
            var jwksUri = new Uri(realmUriWithSlash, "protocol/openid-connect/certs");
            var jwksUrl = jwksUri.ToString();
            _logger.LogDebug("Fetching JWKS from: {JwksUrl}", jwksUrl);

            var response = await _httpClient.GetAsync(jwksUrl);
            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to fetch JWKS from {JwksUrl}. Status: {StatusCode}, Response: {ResponseContent}",
                    jwksUrl, response.StatusCode, responseContent);
                return null;
            }

            var jwksJson = await response.Content.ReadAsStringAsync();
            var jwks = new JsonWebKeySet(jwksJson);

            // Cache for 1 hour
            _jwksCache[cacheKey] = (DateTime.UtcNow.AddHours(1), jwks);

            _logger.LogDebug("Successfully fetched and cached JWKS with {KeyCount} keys", jwks.Keys.Count);
            return jwks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching JWKS from Keycloak");
            return null;
        }
        finally
        {
            _jwksCacheLock.Release();
        }
    }

    public async Task<string> GetManagementApiToken()
    {
        await Task.CompletedTask;
        throw new NotImplementedException("GetManagementApiToken is not implemented for Keycloak. Use Keycloak's management API directly.");
    }
}