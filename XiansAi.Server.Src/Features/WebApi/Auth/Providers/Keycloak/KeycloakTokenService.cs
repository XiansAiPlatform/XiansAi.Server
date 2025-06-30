using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.Tokens;
using RestSharp;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace Features.WebApi.Auth.Providers.Keycloak;

public class KeycloakTokenService : ITokenService
{
    private readonly ILogger<KeycloakTokenService> _logger;
    private RestClient _client;
    private readonly KeycloakConfig? _keycloakConfig;
    private readonly string _organizationClaimType = "organization";

    public KeycloakTokenService(ILogger<KeycloakTokenService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
        _keycloakConfig = configuration.GetSection("Keycloak").Get<KeycloakConfig>() ??
            throw new ArgumentException("Keycloak configuration is missing");
    }

    public string? ExtractUserId(JwtSecurityToken token)
    {
        return token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
    }

    public IEnumerable<string> ExtractTenantIds(JwtSecurityToken token)
    {
        try
        {
            // Find the organization claim
            var organizationClaim = token.Claims.FirstOrDefault(c => c.Type == _organizationClaimType);
            if (organizationClaim == null)
            {
                _logger.LogWarning("No organization claim found in token");
                return Enumerable.Empty<string>();
            }

            // The organization claim contains a JSON object with tenant IDs as properties
            var organizationJson = organizationClaim.Value;
            if (string.IsNullOrEmpty(organizationJson))
            {
                _logger.LogWarning("Organization claim is empty");
                return Enumerable.Empty<string>();
            }

            // Parse the organization JSON
            var tenantIds = new List<string>();
            try
            {
                var organizationObj = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(organizationJson);
                if (organizationObj != null)
                {
                    // Extract tenant IDs which are the keys of the dictionary
                    tenantIds.AddRange(organizationObj.Keys);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse organization claim JSON: {Value}", organizationJson);
            }

            _logger.LogInformation("Extracted tenant IDs from token: {TenantIds}", string.Join(", ", tenantIds));
            return tenantIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting tenant IDs from token");
            return Enumerable.Empty<string>();
        }
    }

    public Task<(bool success, string? userId)> ProcessToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return Task.FromResult<(bool success, string? userId)>((false, null));
            }

            var userId = ExtractUserId(jsonToken);

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("No user identifier found in token");
                return Task.FromResult<(bool success, string? userId)>((false, null));
            }

            return Task.FromResult<(bool success, string? userId)>((true, userId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return Task.FromResult<(bool success, string? userId)>((false, null));
        }
    }

    public async Task<string> GetManagementApiToken()
    {
        try
        {
            if (_keycloakConfig == null || _keycloakConfig.ManagementApi == null)
                throw new InvalidOperationException("Keycloak configuration is not initialized");

            var tokenUrl = $"{_keycloakConfig.AuthServerUrl}/realms/{_keycloakConfig.Realm}/protocol/openid-connect/token";
            _client = new RestClient(tokenUrl);

            var request = new RestRequest("", Method.Post);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");

            request.AddParameter("grant_type", "client_credentials");
            request.AddParameter("client_id", _keycloakConfig.ManagementApi.ClientId ??
                throw new ArgumentException("Management API client ID is missing"));
            request.AddParameter("client_secret", _keycloakConfig.ManagementApi.ClientSecret ??
                throw new ArgumentException("Management API client secret is missing"));

            var response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to get Keycloak admin token: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to get Keycloak admin token: {response.ErrorMessage}");
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(response.Content!);
            return tokenResponse?.AccessToken ?? throw new Exception("No access token in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Keycloak admin token");
            throw;
        }
    }
}