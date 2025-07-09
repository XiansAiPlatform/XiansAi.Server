using Features.WebApi.Auth.Providers.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using XiansAi.Server.Features.WebApi.Services;

namespace Features.WebApi.Auth.Providers.Auth0;

public class Auth0Provider : IAuthProvider
{
    private readonly ILogger<Auth0Provider> _logger;
    private RestClient _client;
    private Auth0Config _auth0Config;
    private readonly Auth0TokenService _tokenService;

    public Auth0Provider(ILogger<Auth0Provider> logger, Auth0TokenService tokenService, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
        _tokenService = tokenService;
        
        // Initialize Auth0 configuration in constructor to ensure it's always available
        _auth0Config = configuration.GetSection("Auth0").Get<Auth0Config>() ?? 
            throw new ArgumentException("Auth0 configuration is missing");
            
        if (string.IsNullOrEmpty(_auth0Config.Domain))
            throw new ArgumentException("Auth0 domain is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // Configuration is already initialized in constructor
        options.RequireHttpsMetadata = false;
        options.Authority = _auth0Config.Domain!.StartsWith("https://") 
            ? _auth0Config.Domain 
            : $"https://{_auth0Config.Domain}/";
        options.Audience = _auth0Config.Audience;
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
            // Extract domain name from URL if it's a full URL
            var domainName = _auth0Config.Domain!.StartsWith("https://") == true 
                ? _auth0Config.Domain.Replace("https://", "").TrimEnd('/')
                : _auth0Config.Domain;

            _client = new RestClient($"https://{domainName}");
            var token = await GetManagementApiToken();
            var request = CreateAuthenticatedRequest($"/api/v2/users/{userId}", Method.Get, token);

            var response = await _client.ExecuteAsync(request);
            EnsureSuccessfulResponse(response, "get user info");

            return JsonSerializer.Deserialize<UserInfo>(response.Content!) ?? 
                throw new Exception("Failed to deserialize user info");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<string> SetNewTenant(string userId, string tenantId)
    {
        try
        {
            var userInfo = await GetUserInfo(userId);
            var appMetadata = userInfo.AppMetadata ?? new AppMetadata { Tenants = Array.Empty<string>() };

            if (appMetadata.Tenants.Contains(tenantId))
            {
                return "Tenant already exists";
            }

            // Extract domain name from URL if it's a full URL
            var domainName = _auth0Config.Domain!.StartsWith("https://") == true 
                ? _auth0Config.Domain.Replace("https://", "").TrimEnd('/')
                : _auth0Config.Domain;

            _client = new RestClient($"https://{domainName}");
            var token = await GetManagementApiToken();
            var request = CreateAuthenticatedRequest($"/api/v2/users/{userId}", Method.Patch, token);

            appMetadata.Tenants = appMetadata.Tenants.Append(tenantId).ToArray();
            request.AddJsonBody(new { app_metadata = appMetadata });

            var response = await _client.ExecuteAsync(request);
            EnsureSuccessfulResponse(response, "update user app_metadata");

            return response.Content ?? "Update successful";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set new tenant {TenantId} for user {UserId}", tenantId, userId);
            throw;
        }
    }

    private async Task<string> GetManagementApiToken()
    {
        return await _tokenService.GetManagementApiToken();
    }

    private RestRequest CreateAuthenticatedRequest(string resource, Method method, string token)
    {
        var request = new RestRequest(resource, method);
        request.AddHeader("authorization", $"Bearer {token}");
        request.AddHeader("content-type", "application/json");
        return request;
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