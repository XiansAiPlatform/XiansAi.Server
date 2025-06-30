using Features.WebApi.Auth.Providers.Auth0;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Features.WebApi.Auth.Providers.Keycloak;

public class KeycloakProvider : IAuthProvider
{
    private readonly ILogger<KeycloakProvider> _logger;
    private RestClient _client;
    private KeycloakConfig _keycloakConfig;
    private readonly KeycloakTokenService _tokenService;

    public KeycloakProvider(ILogger<KeycloakProvider> logger, KeycloakTokenService tokenService, IConfiguration configuration)
    {
        _logger = logger;
        _client = new RestClient();
        _tokenService = tokenService;

        // Initialize Keycloak configuration in constructor
        _keycloakConfig = configuration.GetSection("Keycloak").Get<KeycloakConfig>() ??
            throw new ArgumentException("Keycloak configuration is missing");

        if (string.IsNullOrEmpty(_keycloakConfig.AuthServerUrl))
            throw new ArgumentException("Keycloak server URL is missing");

        if (string.IsNullOrEmpty(_keycloakConfig.Realm))
            throw new ArgumentException("Keycloak realm is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        options.Authority = $"{_keycloakConfig.AuthServerUrl}/realms/{_keycloakConfig.Realm}";

        // Handle multiple audiences by splitting them and configuring TokenValidationParameters
        if (!string.IsNullOrEmpty(_keycloakConfig.Audience))
        {
            var audiences = _keycloakConfig.Audience.Split(',')
                .Select(a => a.Trim())
                .Where(a => !string.IsNullOrEmpty(a))
                .ToArray();

            if (audiences.Length == 1)
            {
                options.Audience = audiences[0];
            }
            else if (audiences.Length > 1)
            {
                // For multiple audiences, set the ValidAudiences property
                options.TokenValidationParameters.ValidAudiences = audiences;
            }
        }

        options.RequireHttpsMetadata = false; // Set to true in production
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
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
            var token = await GetManagementApiToken();

            // Base URL for Keycloak Admin API
            _client = new RestClient($"{_keycloakConfig.AuthServerUrl}/admin/realms/{_keycloakConfig.Realm}");

            var request = new RestRequest($"/users/{userId}", Method.Get);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Content-Type", "application/json");

            var response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to get user info: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to get user info: {response.ErrorMessage}");
            }

            // Parse Keycloak user response
            var keycloakUser = JsonSerializer.Deserialize<KeycloakUserResponse>(response.Content!);
            if (keycloakUser == null)
                throw new Exception("Failed to deserialize Keycloak user info");

            // Convert to common UserInfo format
            var appMetadata = new AppMetadata
            {
                Tenants = keycloakUser.Attributes?.ContainsKey("tenants") == true
                    ? keycloakUser.Attributes["tenants"].ToArray()
                    : Array.Empty<string>()
            };

            return new UserInfo
            {
                UserId = keycloakUser.Id,
                Nickname = keycloakUser.Username,
                AppMetadata = appMetadata,
                CreatedAt = keycloakUser.CreatedTimestamp.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(keycloakUser.CreatedTimestamp.Value).DateTime
                    : DateTime.UtcNow,
                LastLogin = DateTime.UtcNow // Use current time as fallback
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info from Keycloak for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<string> SetNewTenant(string userId, string tenantId)
    {
        try
        {
            var token = await GetManagementApiToken();

            // Base URL for Keycloak Admin API
            _client = new RestClient($"{_keycloakConfig.AuthServerUrl}/admin/realms/{_keycloakConfig.Realm}");

            // Check if organization exists, and create it if it doesn't
            var orgExists = await CheckOrganizationExists(token, tenantId);
            if (!orgExists.Item1)
            {
                // Create the organization if it doesn't exist
                var created = await CreateOrganization(token, tenantId);
                if (!created)
                {
                    _logger.LogError("Failed to create organization {TenantId}", tenantId);
                    throw new Exception($"Failed to create organization {tenantId}");
                }
                _logger.LogInformation("Created new organization {TenantId}", tenantId);
            }

            // Get created organization as the creation does not sent back any data
            orgExists = await CheckOrganizationExists(token, tenantId);

            // Add user as a member of the organization using the specified API endpoint
            var request = new RestRequest($"/organizations/{orgExists.Item2}/members", Method.Post);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Content-Type", "application/json");
            request.AddJsonBody(userId);

            var response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to add user to organization: {ErrorMessage}", response.ErrorMessage);
                throw new Exception($"Failed to add user to organization: {response.ErrorMessage}");
            }

            _logger.LogInformation("Successfully added user {UserId} to organization {TenantId}", userId, tenantId);
            return "Update successful";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set new tenant {TenantId} for Keycloak user {UserId}", tenantId, userId);
            throw;
        }
    }

    private async Task<Tuple<bool, string>> CheckOrganizationExists(string token, string organizationName)
    {
        try
        {
            var request = new RestRequest($"/organizations/?search={organizationName}", Method.Get);
            request.AddHeader("Authorization", $"Bearer {token}");

            var response = await _client.ExecuteAsync(request);

            // Return true if the organization exists (HTTP 200 OK)
            if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            {
                // Parse the JSON response to extract organization ID
                var organizations = JsonSerializer.Deserialize<List<KeycloakOrganization>>(response.Content);
                var existingOrg = organizations?.FirstOrDefault(o => o.Name == organizationName);

                return new Tuple<bool, string>(
                    existingOrg != null,
                    existingOrg?.Id ?? string.Empty
                );
            }

            return new Tuple<bool, string>(false, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if organization {organizationName} exists", organizationName);
            return new Tuple<bool, string>(false, string.Empty);
        }
    }

    private async Task<bool> CreateOrganization(string token, string organizationId)
    {
        try
        {
            var request = new RestRequest("/organizations", Method.Post);
            request.AddHeader("Authorization", $"Bearer {token}");
            request.AddHeader("Content-Type", "application/json");

            // Create organization payload
            var payload = new
            {
                id = organizationId,
                name = organizationId, // You might want to use a more descriptive name
                domains = new[]
            {
                new
                {
                    name = organizationId
                }
            }
            };

            request.AddJsonBody(payload);

            var response = await _client.ExecuteAsync(request);

            if (!response.IsSuccessful)
            {
                _logger.LogError("Failed to create organization: {ErrorMessage}", response.ErrorMessage);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating organization {OrganizationId}", organizationId);
            return false;
        }
    }

    private async Task<string> GetManagementApiToken()
    {
        return await _tokenService.GetManagementApiToken();
    }
}

// Additional classes for Keycloak responses
public class KeycloakUserResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [JsonPropertyName("username")]
    public string Username { get; set; } = default!;

    [JsonPropertyName("createdTimestamp")]
    public long? CreatedTimestamp { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, List<string>>? Attributes { get; set; }
}

public class KeycloakOrganization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}