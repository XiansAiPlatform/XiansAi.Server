using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using XiansAi.Server.Features.WebApi.Services;

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

        if (string.IsNullOrEmpty(_azureB2CConfig.Issuer))
            throw new ArgumentException("Azure B2C issuer is missing");

        if (string.IsNullOrEmpty(_azureB2CConfig.JwksUri))
            throw new ArgumentException("Azure B2C JWKS URI is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // Allow HTTP for development environments
        options.RequireHttpsMetadata = false;

        // // Ensure domain starts with https://
        // var domain = _azureB2CConfig.Domain!.StartsWith("https://") 
        //     ? _azureB2CConfig.Domain 
        //     : $"https://{_azureB2CConfig.Domain}";
            
        // // Azure B2C token issuer does not include policy name, but authority URL for metadata discovery includes it
        // var issuer = $"{domain}/{_azureB2CConfig.TenantId}/v2.0/";
        // var authority = $"{domain}/{_azureB2CConfig.TenantId}/{_azureB2CConfig.Policy}/v2.0/";
        // options.TokenValidationParameters.ValidIssuer = issuer;

        var authority = $"https://sts.windows.net/{_azureB2CConfig.TenantId}/";
        options.Authority = authority;
        options.Audience = _azureB2CConfig.Audience;
        options.TokenValidationParameters.NameClaimType = "name";
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Get user roles from database or token claims
                    var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var tenantId = context.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            using var scope = context.HttpContext.RequestServices.CreateScope();
                            var roleCacheService = scope.ServiceProvider
                                .GetRequiredService<IRoleCacheService>();

                            var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                            foreach (var role in roles)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }
                        }
                    }

                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;
                }
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
        await Task.CompletedTask;
        throw new NotImplementedException();
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