using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;
using System.Security.Claims;
using System.Text.Json;
using XiansAi.Server.Features.WebApi.Services;
using Shared.Auth;
using Shared.Utils;

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

        if (string.IsNullOrEmpty(_azureB2CConfig.Authority))
            throw new ArgumentException("Azure B2C authority is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // Allow HTTP for development environments
        options.RequireHttpsMetadata = false;

        // Extract policy name from JWKS URI for Azure B2C authority
        // JWKS URI format: https://domain/tenant/policy/discovery/v2.0/keys
        // Authority should be: https://domain/tenant/policy/v2.0/
        
        options.Authority = _azureB2CConfig.Authority;
        options.TokenValidationParameters.ValidIssuer = _azureB2CConfig.Issuer;
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
                    
                    // If NameIdentifier is not found, try the same extraction logic as AzureB2CTokenService
                    if (string.IsNullOrEmpty(userId))
                    {
                        // First try 'oid' claim (Azure B2C object ID)
                        userId = identity.FindFirst("oid")?.Value;
                        
                        // Fallback to 'sub' claim (standard JWT user ID) - Azure Entra ID uses this
                        if (string.IsNullOrEmpty(userId))
                        {
                            userId = identity.FindFirst("sub")?.Value;
                        }
                        
                        if (!string.IsNullOrEmpty(userId))
                        {
                            // Add the NameIdentifier claim so other parts of the system can find it
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var tenantId = context.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            using var scope = context.HttpContext.RequestServices.CreateScope();
                            var roleCacheService = scope.ServiceProvider
                                .GetRequiredService<IRoleCacheService>();

                            var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                            //handle role for default tenant
                            if(tenantId == Constants.DefaultTenantId)
                            {
                                if(roles == null)
                                {
                                    roles = new List<string>();
                                }
                                
                                if (!roles.Contains(SystemRoles.TenantUser))
                                {
                                    roles.Add(SystemRoles.TenantUser);
                                }
                            }

                            foreach (var role in roles)
                            {
                                // Prevent duplicate role claims
                                if (!identity.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role))
                                {
                                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                                }
                            }
                        }
                        else
                        {
                            // No tenant ID header provided - assign default TenantUser role to allow basic access
                            if (!identity.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == SystemRoles.TenantUser))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, SystemRoles.TenantUser));
                            }
                        }
                    }

                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;
                }
            }
        };
    }

    public Task<(bool success, string? userId)> ValidateToken(string token)
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

    //Only Valid for Auth0 for backward compatibility. To be removed.
    public Task<List<string>> GetUserTenants(string userId)
    {
        return Task.FromResult(new List<string>());
    }
} 