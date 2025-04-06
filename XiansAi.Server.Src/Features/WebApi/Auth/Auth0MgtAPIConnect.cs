using RestSharp;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Features.WebApi.Auth;

public class Auth0Config
{
    public string? Domain { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
}

public class ManagementApiConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class AppMetadata
{
    [JsonPropertyName("tenants")]
    public required string[] Tenants { get; set; } = [];
}

public interface IAuth0MgtAPIConnect    
{
    /// <summary>
    /// Gets the Auth0 Management API token
    /// </summary>
    Task<string> GetManagementApiToken();
    
    /// <summary>
    /// Adds a new tenant to the user's app_metadata
    /// </summary>
    Task<string> SetNewTenant(string userId, string tenantId);
    
    /// <summary>
    /// Gets user information from Auth0
    /// </summary>
    Task<UserInfo> GetUserInfo(string userId);
}

public class Auth0MgtAPIConnect : IAuth0MgtAPIConnect
{
    private readonly Auth0Config _auth0Config;
    private readonly ILogger<Auth0MgtAPIConnect> _logger;
    private readonly RestClient _client;

    public Auth0MgtAPIConnect(IConfiguration configuration, ILogger<Auth0MgtAPIConnect> logger)
    {
        _logger = logger;
        _auth0Config = configuration.GetSection("Auth0").Get<Auth0Config>() ?? 
            throw new ArgumentException("Auth0 configuration is missing");
        
        if (string.IsNullOrEmpty(_auth0Config.Domain))
            throw new ArgumentException("Auth0 domain is missing");
            
        _client = new RestClient($"https://{_auth0Config.Domain}");
    }

    public async Task<UserInfo> GetUserInfo(string userId)
    {
        try
        {
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

    public async Task<string> GetManagementApiToken()
    {
        try
        {
            var request = new RestRequest("/oauth/token", Method.Post);
            request.AddHeader("content-type", "application/x-www-form-urlencoded");

            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_id", _auth0Config.ManagementApi?.ClientId ?? 
                throw new ArgumentException("Management API client ID is missing"));
            request.AddParameter("client_secret", _auth0Config.ManagementApi?.ClientSecret ?? 
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

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }
    
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("app_metadata")]
    public AppMetadata? AppMetadata { get; set; }

    [JsonPropertyName("last_login")]
    public DateTime LastLogin { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_ip")]
    public string? LastIp { get; set; }

}