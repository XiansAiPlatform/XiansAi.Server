using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Features.WebApi.Auth.Providers.AzureB2C;
using Features.WebApi.Auth.Providers.Auth0;
using RestSharp;

namespace Features.WebApi.Auth.Providers.Tokens;

public class AzureB2CTokenService : ITokenService
{
    private readonly ILogger<AzureB2CTokenService> _logger;
    private RestClient _client;
    private AzureB2CConfig? _azureB2CConfig;
    private readonly string _tenantClaimType = "extension_tenants";

    public AzureB2CTokenService(ILogger<AzureB2CTokenService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
        _azureB2CConfig = configuration.GetSection("AzureB2C").Get<AzureB2CConfig>() ??
            throw new ArgumentException("Azure B2C configuration is missing");
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        return token.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
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
            if (_azureB2CConfig == null || _azureB2CConfig.ManagementApi == null)
                throw new InvalidOperationException("Azure B2C configuration is not initialized");

            _client = new RestClient($"https://login.microsoftonline.com/{_azureB2CConfig.TenantId}/oauth2/v2.0/token");
            
            var request = new RestRequest("", Method.Post);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            
            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_id", _azureB2CConfig.ManagementApi.ClientId ?? 
                throw new ArgumentException("Management API client ID is missing"));
            request.AddParameter("client_secret", _azureB2CConfig.ManagementApi.ClientSecret ?? 
                throw new ArgumentException("Management API client secret is missing"));
            request.AddParameter("scope", "https://graph.microsoft.com/.default");
            
            var response = await _client.ExecuteAsync(request);
            
            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to get MS Graph API token: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to get MS Graph API token: {response.ErrorMessage}");
            }
            
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(response.Content!);
            return tokenResponse?.AccessToken ?? throw new Exception("No access token in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get MS Graph API token");
            throw;
        }
    }
} 