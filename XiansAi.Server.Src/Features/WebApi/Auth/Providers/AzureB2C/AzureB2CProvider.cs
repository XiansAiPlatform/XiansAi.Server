using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Features.WebApi.Auth.Providers.Auth0;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;

namespace Features.WebApi.Auth.Providers.AzureB2C;

public class AzureB2CProvider : IAuthProvider
{
    private readonly ILogger<AzureB2CProvider> _logger;
    private RestClient _client;
    private AzureB2CConfig? _azureB2CConfig;
    private readonly string _tenantClaimType = "extension_tenants";

    public AzureB2CProvider(ILogger<AzureB2CProvider> logger)
    {
        _logger = logger;
        _client = new RestClient();
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        _azureB2CConfig = configuration.GetSection("AzureB2C").Get<AzureB2CConfig>() ??
            throw new ArgumentException("Azure B2C configuration is missing");

        if (string.IsNullOrEmpty(_azureB2CConfig.TenantId))
            throw new ArgumentException("Azure B2C tenant ID is missing");

        if (string.IsNullOrEmpty(_azureB2CConfig.Domain))
            throw new ArgumentException("Azure B2C domain is missing");

        options.Authority = $"{_azureB2CConfig.Instance}/{_azureB2CConfig.TenantId}/{_azureB2CConfig.Policy}/v2.0/";
        options.Audience = _azureB2CConfig.Audience;
        options.TokenValidationParameters.NameClaimType = "name";
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;
                }
                return Task.CompletedTask;
            }
        };
    }

    public Task<(bool success, string? userId, IEnumerable<string>? tenantIds)> ValidateToken(string token)
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

            var userId = jsonToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
            }

            var tenantIds = jsonToken.Claims
                .Where(c => c.Type == _tenantClaimType)
                .Select(c => c.Value)
                .ToList();
            
            return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((true, userId, tenantIds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
        }
    }

    public async Task<UserInfo> GetUserInfo(string userId)
    {
        try
        {
            if (_azureB2CConfig == null || _azureB2CConfig.ManagementApi == null)
                throw new InvalidOperationException("Azure B2C configuration is not initialized");

            var azureUserInfo = await GetAzureB2CUserInfo(userId);
            
            // Convert Azure B2C user info to common UserInfo format
            return new UserInfo
            {
                UserId = azureUserInfo.UserId,
                Nickname = azureUserInfo.DisplayName,
                AppMetadata = new AppMetadata 
                { 
                    Tenants = azureUserInfo.Tenants ?? Array.Empty<string>() 
                },
                CreatedAt = DateTime.UtcNow,  // Azure B2C doesn't provide this directly
                LastLogin = DateTime.UtcNow   // Azure B2C doesn't provide this directly
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info from Azure B2C for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<string> SetNewTenant(string userId, string tenantId)
    {
        try
        {
            if (_azureB2CConfig == null || _azureB2CConfig.ManagementApi == null)
                throw new InvalidOperationException("Azure B2C configuration is not initialized");

            var azureUserInfo = await GetAzureB2CUserInfo(userId);
            
            var tenants = azureUserInfo.Tenants?.ToList() ?? new List<string>();
            
            if (tenants.Contains(tenantId))
            {
                return "Tenant already exists";
            }
            
            tenants.Add(tenantId);
            
            // Get access token for MS Graph API
            var token = await GetMsGraphApiToken();
            
            // Base URL for Microsoft Graph API
            _client = new RestClient("https://graph.microsoft.com/v1.0");
            
            var request = new RestRequest($"/users/{userId}", Method.Patch);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Content-Type", "application/json");
            
            // Create the JSON payload with the custom extension attribute
            // Note: The extension name must be registered in your B2C tenant
            request.AddJsonBody(new
            {
                extension_tenants = tenants.ToArray()
            });
            
            var response = await _client.ExecuteAsync(request);
            
            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to update user: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to update user: {response.ErrorMessage}");
            }
            
            return "Update successful";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set new tenant {TenantId} for Azure B2C user {UserId}", tenantId, userId);
            throw;
        }
    }

    private async Task<AzureB2CUserInfo> GetAzureB2CUserInfo(string userId)
    {
        var token = await GetMsGraphApiToken();
            
        // Base URL for Microsoft Graph API
        _client = new RestClient("https://graph.microsoft.com/v1.0");
        
        var request = new RestRequest($"/users/{userId}", Method.Get);
        request.AddHeader("Authorization", $"Bearer {token}");
        request.AddHeader("Content-Type", "application/json");
        
        var response = await _client.ExecuteAsync(request);
        
        if (!response.IsSuccessful)
        {
            _logger.LogError("Failed to get user info: {ErrorMessage}", response.ErrorMessage);
            throw new Exception($"Failed to get user info: {response.ErrorMessage}");
        }
        
        return JsonSerializer.Deserialize<AzureB2CUserInfo>(response.Content!) ??
            throw new Exception("Failed to deserialize Azure B2C user info");
    }

    private async Task<string> GetMsGraphApiToken()
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