using Microsoft.AspNetCore.Authentication.JwtBearer;
using RestSharp;
using Shared.Utils;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IdentityModel.Tokens.Jwt;
using Shared.Providers.Auth.Auth0;
using Shared.Services;

namespace Shared.Providers.Auth.Keycloak;

public class KeycloakProvider : IAuthProvider
{
    private readonly ILogger<KeycloakProvider> _logger;
    private RestClient _client;
    private KeycloakConfig _keycloakConfig;
    private readonly KeycloakTokenService _tokenService;

    public KeycloakProvider(ILogger<KeycloakProvider> logger, KeycloakTokenService tokenService, IConfiguration configuration, ApiType api = ApiType.WebApi)
    {
        _logger = logger;
        _client = new RestClient();
        _tokenService = tokenService;

        // Load config for WebApi/UserApi
        string sectionName = api == ApiType.UserApi ? "UserApi" : "WebApi";
        var section = configuration.GetSection(sectionName);
        _keycloakConfig = section.GetSection("Keycloak").Get<KeycloakConfig>() ??
            throw new ArgumentException($"Keycloak configuration is missing for {sectionName}");
        // Initialize Keycloak configuration in constructor
        //_keycloakConfig = configuration.GetSection("Keycloak").Get<KeycloakConfig>() ??
        //    throw new ArgumentException("Keycloak configuration is missing");

        if (string.IsNullOrEmpty(_keycloakConfig.AuthServerUrl))
            throw new ArgumentException("Keycloak server URL is missing");

        if (string.IsNullOrEmpty(_keycloakConfig.Realm))
            throw new ArgumentException("Keycloak realm is missing");
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        var baseUri = new Uri(_keycloakConfig.AuthServerUrl ?? throw new InvalidOperationException("Keycloak configuration is missing"));
        var authorityUri = new Uri(baseUri, $"realms/{_keycloakConfig.Realm}");
        options.Authority = authorityUri.ToString();

        options.RequireHttpsMetadata = false; // Set to true in production
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

        // Configure audience validation for Keycloak
        // Keycloak typically uses 'account' as the audience for standard tokens
        options.TokenValidationParameters.ValidAudiences = new[] { "account" };

        // Map claims properly for Keycloak
        options.MapInboundClaims = false; // Prevent automatic claim type mapping

        // Set up claim type mapping for proper user identification
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear(); // Clear default mappings
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Add("sub", ClaimTypes.NameIdentifier);
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Add("preferred_username", ClaimTypes.Name);

        // Add JWT Bearer events to debug and properly authenticate the user
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                _logger.LogError("JWT Bearer authentication failed: {Exception}", context.Exception?.Message);
                _logger.LogError("Exception details: {FullException}", context.Exception?.ToString());
                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                _logger.LogWarning("JWT Bearer challenge triggered: {Error}, {ErrorDescription}",
                    context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    _logger.LogInformation("Original identity authenticated: {IsAuthenticated}, Type: {AuthenticationType}",
                        identity.IsAuthenticated, identity.AuthenticationType);

                    // Ensure the identity is properly authenticated
                    if (!identity.IsAuthenticated)
                    {
                        _logger.LogInformation("Creating new authenticated identity with JWT authentication type");
                        // Create a new authenticated identity
                        var authenticatedIdentity = new ClaimsIdentity(identity.Claims, "JWT", identity.NameClaimType, identity.RoleClaimType);
                        context.Principal = new ClaimsPrincipal(authenticatedIdentity);
                        identity = authenticatedIdentity;

                        _logger.LogInformation("New identity authenticated: {IsAuthenticated}, Type: {AuthenticationType}",
                            authenticatedIdentity.IsAuthenticated, authenticatedIdentity.AuthenticationType);
                    }

                    // Get user roles from database or token claims (matching Auth0 behavior)
                    var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                    // If NameIdentifier is not found, try the same extraction logic as KeycloakTokenService
                    if (string.IsNullOrEmpty(userId))
                    {
                        // First try 'sub' claim (standard JWT user ID)
                        userId = identity.FindFirst("sub")?.Value;

                        // Fallback to 'preferred_username' for Keycloak tokens
                        if (string.IsNullOrEmpty(userId))
                        {
                            userId = identity.FindFirst("preferred_username")?.Value;
                        }

                        if (!string.IsNullOrEmpty(userId))
                        {
                            _logger.LogInformation("Extracted user ID for role assignment: {UserId}", userId);
                            // Add the NameIdentifier claim so other parts of the system can find it
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                        }
                    }

                    var tenantId = context.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

                    if (!string.IsNullOrEmpty(userId))
                    {

                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            using var scope = context.HttpContext.RequestServices.CreateScope();
                            var roleCacheService = scope.ServiceProvider
                                .GetRequiredService<IRoleCacheService>();

                            var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                            //handle role for default tenant
                            if (tenantId == Constants.DefaultTenantId)
                            {
                                if (roles == null)
                                {
                                    roles = new List<string>();
                                }
                                roles.Add(SystemRoles.TenantUser);
                            }

                            foreach (var role in roles)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }

                            _logger.LogInformation("Added {RoleCount} roles to Keycloak user {UserId}: {Roles}",
                                roles.Count(), userId, string.Join(", ", roles));
                        }
                        else
                        {
                            // No tenant ID header provided - assign default TenantUser role to allow basic access
                            _logger.LogInformation("No X-Tenant-Id header found, assigning default TenantUser role to user {UserId}", userId);
                            identity.AddClaim(new Claim(ClaimTypes.Role, SystemRoles.TenantUser));
                        }
                    }

                    // Set the User property of HttpContext to ensure it's properly authenticated
                    context.HttpContext.User = context.Principal;

                    _logger.LogInformation("Keycloak user authenticated: {UserId}, IsAuthenticated: {IsAuthenticated}",
                        identity.Name, context.HttpContext.User.Identity?.IsAuthenticated);
                }
                else
                {
                    _logger.LogWarning("No principal or identity found in OnTokenValidated");
                }
            }
        };
    }

    public async Task<(bool success, string? userId)> ValidateToken(string token)
    {
        return await _tokenService.ProcessToken(token);
    }

    public async Task<UserInfo> GetUserInfo(string userId)
    {
        try
        {
            var token = await GetManagementApiToken();

            // Base URL for Keycloak Admin API
            var baseUri = new Uri(_keycloakConfig.AuthServerUrl ?? throw new InvalidOperationException("Keycloak configuration is missing"));
            var adminUri = new Uri(baseUri, $"admin/realms/{_keycloakConfig.Realm}");
            _client = new RestClient(adminUri.ToString());

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
                    : new List<string> { Constants.DefaultTenantId }.ToArray()
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
            var baseUri = new Uri(_keycloakConfig.AuthServerUrl ?? throw new InvalidOperationException("Keycloak configuration is missing"));
            var adminUri = new Uri(baseUri, $"admin/realms/{_keycloakConfig.Realm}");
            _client = new RestClient(adminUri.ToString());

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

    //Only Valid for Auth0 for backward compatibility. To be removed.
    public Task<List<string>> GetUserTenants(string userId)
    {
        return Task.FromResult(new List<string>());
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