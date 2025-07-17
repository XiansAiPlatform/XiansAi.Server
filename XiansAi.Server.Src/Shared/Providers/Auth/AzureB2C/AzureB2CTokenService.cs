using System.IdentityModel.Tokens.Jwt;
using RestSharp;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Cryptography;
using Shared.Utils;


namespace Shared.Providers.Auth.AzureB2C;

public class AzureB2CTokenService : ITokenService
{
    private readonly ILogger<AzureB2CTokenService> _logger;
    private AzureB2CConfig? _azureB2CConfig;
    private readonly string _tenantClaimType;
    private readonly HttpClient _httpClient;
    private static readonly Dictionary<string, (DateTime expiry, JsonWebKeySet jwks)> _jwksCache = new();
    private static readonly SemaphoreSlim _jwksCacheLock = new(1, 1);
    private readonly ApiType _apiType;

    public AzureB2CTokenService(ILogger<AzureB2CTokenService> logger, IConfiguration configuration, HttpClient? httpClient = null, ApiType api = ApiType.WebApi)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _apiType = api;
        // Load config for WebApi/UserApi
        string sectionName = api == ApiType.UserApi ? "UserApi" : "WebApi";
        var section = configuration.GetSection(sectionName);
        _azureB2CConfig = section.GetSection("AzureB2C").Get<AzureB2CConfig>() ??
            throw new ArgumentException($"Azure B2C configuration is missing for {sectionName}");

        var authProviderConfig = section.GetSection("AuthProvider").Get<AuthProviderConfig>() ??
            new AuthProviderConfig();
        _tenantClaimType = authProviderConfig.TenantClaimType;
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        var userId = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            userId = token.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
        }
        return userId;
    }

    public IEnumerable<string> ExtractTenantIds(JwtSecurityToken token)
    {
        var tenantIds = token.Claims
            .Where(c => c.Type == _tenantClaimType)
            .Select(c => c.Value)
            .ToList();

        if (tenantIds.Count == 0)
        {
            _logger.LogWarning("No tenant IDs found in token");
            return new List<string> { Constants.DefaultTenantId };
        }

        return tenantIds;
    }

    public async Task<(bool success, string? userId)> ProcessToken(string token)
    {
        try
        {
            // SECURITY FIX: Validate the JWT token with JWKS before processing claims
            var validationResult = await ValidateJwtWithJwks(token);
            if (!validationResult.success)
            {
                _logger.LogWarning("JWT token validation failed: {Error}", validationResult.errorMessage);
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
            if (_azureB2CConfig == null)
            {
                return (false, "Azure B2C configuration is missing");
            }

            // Get JWKS
            var jwks = await GetJwks();
            if (jwks == null)
            {
                return (false, "Failed to fetch JWKS from Azure B2C");
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
            var validationParameters = new TokenValidationParameters
            {
                ValidateAudience = !string.IsNullOrEmpty(_azureB2CConfig.Audience),
                ValidAudience = _azureB2CConfig.Audience,
                ValidateIssuer = true,
                //ValidIssuer = $"https://sts.windows.net/{_azureB2CConfig.TenantId}/", // Azure Entra ID actual token issuer
                //ValidIssuer = $"{domain}/{_azureB2CConfig.TenantId}/v2.0/", // Token issuer does not include policy
                ValidIssuer = _azureB2CConfig.Issuer,
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
        if (_azureB2CConfig == null)
        {
            return null;
        }

        // Ensure domain starts with https://
        // var domain = _azureB2CConfig.Domain!.StartsWith("https://") 
        //     ? _azureB2CConfig.Domain 
        //     : $"https://{_azureB2CConfig.Domain}";
        // Azure B2C JWKS URL includes the policy
        //var jwksUri = $"{domain}/{_azureB2CConfig.TenantId}/{_azureB2CConfig.Policy}/discovery/v2.0/keys";
        // Azure Entra ID JWKS URL format
        //var jwksUri = $"https://login.microsoftonline.com/{_azureB2CConfig.TenantId}/discovery/v2.0/keys";
        var jwksUri = _azureB2CConfig.JwksUri ?? throw new InvalidOperationException("JWKS URI is not configured");
        var cacheKey = jwksUri;

        await _jwksCacheLock.WaitAsync();
        try
        {
            // Check cache first
            if (_jwksCache.TryGetValue(cacheKey, out var cached) && cached.expiry > DateTime.UtcNow)
            {
                return cached.jwks;
            }

            // Fetch fresh JWKS
            _logger.LogDebug("Fetching JWKS from: {JwksUrl}", jwksUri);

            var response = await _httpClient.GetAsync(jwksUri);
            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to fetch JWKS from {JwksUrl}. Status: {StatusCode}, Response: {ResponseContent}",
                    jwksUri, response.StatusCode, responseContent);
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
            _logger.LogError(ex, "Error fetching JWKS from Azure B2C");
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
        throw new NotImplementedException();
    }
}