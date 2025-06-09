using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;

namespace Features.WebApi.Auth.Providers.AzureB2C;

public class AzureB2CProvider : IAuthProvider
{
    private readonly ILogger<AzureB2CProvider> _logger;
    private RestClient _client;
    private AzureB2CConfig _azureB2CConfig;
    private readonly AzureB2CTokenService _tokenService;

    public AzureB2CProvider(ILogger<AzureB2CProvider> logger, AzureB2CTokenService tokenService, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
        _tokenService = tokenService;
        
        // Initialize Azure B2C configuration in constructor to ensure it's always available
        _azureB2CConfig = configuration.GetSection("AzureB2C").Get<AzureB2CConfig>() ??
            throw new ArgumentException("Azure B2C configuration is missing");

        if (string.IsNullOrEmpty(_azureB2CConfig.TenantId))
            throw new ArgumentException("Azure B2C tenant ID is missing");

        if (string.IsNullOrEmpty(_azureB2CConfig.Domain))
            throw new ArgumentException("Azure B2C domain is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // Configuration is already initialized in constructor
        // Use the custom domain for issuer validation to match the actual token issuer
        options.TokenValidationParameters.ValidIssuer = $"{_azureB2CConfig.Domain}/{_azureB2CConfig.TenantId}/v2.0/";
        // Use the custom domain for authority to match the issuer and enable proper key discovery
        options.Authority = $"{_azureB2CConfig.Domain}/{_azureB2CConfig.TenantId}/{_azureB2CConfig.Policy}/v2.0/";
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
        return _tokenService.ProcessToken(token);
    }

    public async Task<UserInfo> GetUserInfo(string userId)
    {
        try
        {
            if (_azureB2CConfig.ManagementApi == null)
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
            if (_azureB2CConfig.ManagementApi == null)
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
        return await _tokenService.GetManagementApiToken();
    }
} 