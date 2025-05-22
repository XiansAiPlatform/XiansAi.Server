using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;

namespace Features.WebApi.Auth.Providers.Auth0;

public class Auth0Provider : IAuthProvider
{
    private readonly ILogger<Auth0Provider> _logger;
    private RestClient _client;
    private Auth0Config? _auth0Config;

    public Auth0Provider(ILogger<Auth0Provider> logger)
    {
        _logger = logger;
        _client = new RestClient();
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        _auth0Config = configuration.GetSection("Auth0").Get<Auth0Config>() ?? 
            throw new ArgumentException("Auth0 configuration is missing");
            
        if (string.IsNullOrEmpty(_auth0Config.Domain))
            throw new ArgumentException("Auth0 domain is missing");
            
        options.RequireHttpsMetadata = false;
        options.Authority = _auth0Config.Domain;
        options.Audience = _auth0Config.Audience;
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

            var userId = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return Task.FromResult<(bool success, string? userId, IEnumerable<string>? tenantIds)>((false, null, null));
            }

            var tenantIds = jsonToken.Claims
                .Where(c => c.Type == BaseAuthRequirement.TENANT_CLAIM_TYPE)
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
            if (_auth0Config == null)
                throw new InvalidOperationException("Auth0 configuration is not initialized");

            _client = new RestClient($"https://{_auth0Config.Domain}");
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

            if (_auth0Config == null)
                throw new InvalidOperationException("Auth0 configuration is not initialized");

            _client = new RestClient($"https://{_auth0Config.Domain}");
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
        try
        {
            if (_auth0Config == null || _auth0Config.ManagementApi == null)
                throw new InvalidOperationException("Auth0 configuration is not initialized");

            _client = new RestClient($"https://{_auth0Config.Domain}");
            var request = new RestRequest("/oauth/token", Method.Post);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");

            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_id", _auth0Config.ManagementApi.ClientId ?? 
                throw new ArgumentException("Management API client ID is missing"));
            request.AddParameter("client_secret", _auth0Config.ManagementApi.ClientSecret ?? 
                throw new ArgumentException("Management API client secret is missing"));
            request.AddParameter("audience", $"https://{_auth0Config.Domain}/api/v2/");

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