using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Features.WebApi.Auth.Providers.Auth0;
using RestSharp;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Cryptography;
using System.Security.Claims;
using Shared.Utils;

namespace Features.WebApi.Auth.Providers.Tokens;

public class Auth0TokenService : ITokenService
{
    private readonly ILogger<Auth0TokenService> _logger;
    private RestClient _client;
    private Auth0Config? _auth0Config;
    private readonly string _tenantClaimType;
    private readonly HttpClient _httpClient;
    private static readonly Dictionary<string, (DateTime expiry, JsonWebKeySet jwks)> _jwksCache = new();
    private static readonly SemaphoreSlim _jwksCacheLock = new(1, 1);

    public Auth0TokenService(ILogger<Auth0TokenService> logger, IConfiguration configuration, HttpClient? httpClient = null)
    {
        _logger = logger;
        _client = new RestClient();
        _httpClient = httpClient ?? new HttpClient();
        _auth0Config = configuration.GetSection("Auth0").Get<Auth0Config>() ?? 
            throw new ArgumentException("Auth0 configuration is missing");
        
        var authProviderConfig = configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? 
            new AuthProviderConfig();
        _tenantClaimType = authProviderConfig.TenantClaimType;
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        return token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
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

    public async Task<(bool success, string? userId, IEnumerable<string>? tenantIds)> ProcessToken(string token)
    {
        try
        {
            // SECURITY FIX: Validate the JWT token with JWKS before processing claims
            var validationResult = await ValidateJwtWithJwks(token);
            if (!validationResult.success)
            {
                _logger.LogWarning("JWT token validation failed: {Error}", validationResult.errorMessage);
                return (false, null, null);
            }

            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format: {Token}", token);
                return (false, null, null);
            }

            var userId = ExtractUserId(jsonToken);
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return (false, null, null);
            }

            var tenantIds = ExtractTenantIds(jsonToken);
            _logger.LogInformation("Tenant IDs: {TenantIds}", string.Join(", ", tenantIds));
            
            return (true, userId, tenantIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null);
        }
    }

    private async Task<(bool success, string? errorMessage)> ValidateJwtWithJwks(string token)
    {
        try
        {
            if (_auth0Config == null)
            {
                return (false, "Auth0 configuration is missing");
            }

            // Get JWKS
            var jwks = await GetJwks();
            if (jwks == null)
            {
                return (false, "Failed to fetch JWKS from Auth0");
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

            // Ensure domain starts with https://
            var domain = _auth0Config.Domain!.StartsWith("https://") 
                ? _auth0Config.Domain 
                : $"https://{_auth0Config.Domain}/";

            // Set up token validation parameters
            var validationParameters = new TokenValidationParameters
            {
                // Audience validation
                ValidateAudience = !string.IsNullOrEmpty(_auth0Config.Audience),
                ValidAudience = _auth0Config.Audience,
                RequireAudience = !string.IsNullOrEmpty(_auth0Config.Audience),
                
                // Issuer validation
                ValidateIssuer = true,
                ValidIssuer = domain,
                
                // Lifetime validation
                ValidateLifetime = true,
                RequireExpirationTime = true,
                
                // Signing key validation
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                IssuerSigningKey = rsaSecurityKey,
                
                // Clock skew tolerance
                ClockSkew = TimeSpan.FromMinutes(5),
                
                // Claim type mappings for proper role and name claim handling
                NameClaimType = "sub",
                RoleClaimType = ClaimTypes.Role
            };

            // Validate the token
            _logger.LogDebug("Validating JWT token with issuer: {Issuer}, audience: {Audience}", 
                validationParameters.ValidIssuer, validationParameters.ValidAudience);
                
            var result = await handler.ValidateTokenAsync(token, validationParameters);
            
            if (!result.IsValid)
            {
                var errorMessage = result.Exception?.Message ?? "Token validation failed";
                _logger.LogWarning("JWT validation failed: {ErrorMessage}", errorMessage);
                if (result.Exception != null)
                {
                    _logger.LogWarning("JWT validation exception details: {ExceptionType}: {ExceptionMessage}", 
                        result.Exception.GetType().Name, result.Exception.Message);
                }
                return (false, errorMessage);
            }

            _logger.LogDebug("JWT token validated successfully with issuer: {Issuer} and audience: {Audience}", 
                validationParameters.ValidIssuer, validationParameters.ValidAudience);
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
        if (_auth0Config == null)
        {
            return null;
        }

        // Extract domain name from URL if it's a full URL
        var domainName = _auth0Config.Domain?.StartsWith("https://") == true 
            ? _auth0Config.Domain.Replace("https://", "").TrimEnd('/')
            : _auth0Config.Domain;

        var jwksUri = $"https://{domainName}/.well-known/jwks.json";
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
            _logger.LogError(ex, "Error fetching JWKS from Auth0");
            return null;
        }
        finally
        {
            _jwksCacheLock.Release();
        }
    }

    public async Task<string> GetManagementApiToken()
    {
        try
        {
            if (_auth0Config == null || _auth0Config.ManagementApi == null)
                throw new InvalidOperationException("Auth0 configuration is not initialized");

            // Extract domain name from URL if it's a full URL
            var domainName = _auth0Config.Domain?.StartsWith("https://") == true 
                ? _auth0Config.Domain.Replace("https://", "").TrimEnd('/')
                : _auth0Config.Domain;

            _client = new RestClient($"https://{domainName}");
            var request = new RestRequest("/oauth/token", Method.Post);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");

            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_id", _auth0Config.ManagementApi.ClientId ?? 
                throw new ArgumentException("Management API client ID is missing"));
            request.AddParameter("client_secret", _auth0Config.ManagementApi.ClientSecret ?? 
                throw new ArgumentException("Management API client secret is missing"));
            request.AddParameter("audience", $"https://{domainName}/api/v2/");

            var response = await _client.ExecuteAsync(request);
            EnsureSuccessfulResponse(response, "get management API token");

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(response.Content!);
            return tokenResponse?.AccessToken ?? throw new Exception("No access token in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get management API token");
            throw;
        }
    }
    
    private void EnsureSuccessfulResponse(RestResponse response, string operation)
    {
        if (!response.IsSuccessful)
        {
            _logger.LogError("Failed to {Operation}: {ErrorMessage}", operation, response.ErrorMessage);
            throw new Exception($"Failed to {operation}: {response.ErrorMessage}");
        }
    }
} 