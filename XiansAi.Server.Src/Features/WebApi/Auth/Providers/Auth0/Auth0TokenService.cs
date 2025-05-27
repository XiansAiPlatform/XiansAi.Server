using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Features.WebApi.Auth.Providers.Auth0;
using RestSharp;

namespace Features.WebApi.Auth.Providers.Tokens;

public class Auth0TokenService : ITokenService
{
    private readonly ILogger<Auth0TokenService> _logger;
    private RestClient _client;
    private Auth0Config? _auth0Config;
    private readonly string _tenantClaimType;

    public Auth0TokenService(ILogger<Auth0TokenService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
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
        return token.Claims
            .Where(c => c.Type == _tenantClaimType)
            .Select(c => c.Value)
            .ToList();
    }

    public Task<(bool success, string? userId, IEnumerable<string>? tenantIds)> ProcessToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
            }

            var userId = ExtractUserId(jsonToken);
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
            }

            var tenantIds = ExtractTenantIds(jsonToken);
            
            return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((true, userId, tenantIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
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