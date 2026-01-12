using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using Shared.Repositories;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Shared.Data.Models;
using Features.AdminApi.Constants;

namespace Features.AdminApi.Auth
{
    public class AdminEndpointAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<AdminEndpointAuthenticationHandler> _logger;
        private readonly IApiKeyService _apiKeyService;
        private readonly IUserRepository _userRepository;
        private readonly ITenantRepository _tenantRepository;

        public AdminEndpointAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService,
            IUserRepository userRepository,
            ITenantRepository tenantRepository)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<AdminEndpointAuthenticationHandler>();
            _tenantContext = tenantContext;
            _apiKeyService = apiKeyService;
            _userRepository = userRepository;
            _tenantRepository = tenantRepository;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Only handle authentication for AdminApi endpoints
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            var adminApiBasePath = AdminApiConstants.GetVersionedBasePath().ToLowerInvariant();
            
            _logger.LogDebug("AdminEndpointAuthenticationHandler: Evaluating path '{Path}'", Request.Path);
            
            if (!path.StartsWith(adminApiBasePath))
            {
                _logger.LogDebug("Skipping admin endpoint authentication for non-AdminApi path: {Path}", Request.Path);
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }

            _logger.LogDebug("Processing AdminApi endpoint request: {Path}", Request.Path);

            // Extract API key from Authorization header first, then fallback to query parameter
            var accessToken = string.Empty;
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                accessToken = authHeader.Substring("Bearer ".Length).Trim();
            }
            
            // Fallback to query parameter if not found in Authorization header
            if (string.IsNullOrEmpty(accessToken))
            {
                accessToken = Request.Query["apikey"].ToString();
            }
            
            // Extract tenantId from multiple sources in priority order:
            // 1. Query parameter (tenantId=)
            // 2. Route parameter (e.g., /tenants/{tenantId})
            // 3. X-Tenant-Id header
            var tenantId = Request.Query["tenantId"].ToString();
            if (string.IsNullOrEmpty(tenantId))
            {
                // Try route parameter
                if (Request.RouteValues.TryGetValue("tenantId", out var routeTenantId) && routeTenantId != null)
                {
                    tenantId = routeTenantId.ToString() ?? string.Empty;
                }
            }
            if (string.IsNullOrEmpty(tenantId))
            {
                // Try X-Tenant-Id header
                tenantId = Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? string.Empty;
            }
            if (string.IsNullOrEmpty(tenantId))
            {
                return AuthenticateResult.Fail("Tenant ID is required");
            }
            
            // Note: tenantId is now optional. If not provided, it will be derived from the API key.
            // This prevents IDOR vulnerabilities by ensuring the tenant matches the authenticated credential.

            _logger.LogDebug("Processing AdminApi Endpoint request: {Path}", Request.Path);
            if (_tenantContext != null)
            {

                if (!string.IsNullOrEmpty(accessToken))
                {
                    try
                    {
                        // Only API keys starting with "sk-Xnai-" are supported
                        if (!accessToken.StartsWith("sk-Xnai-"))
                        {
                            _logger.LogWarning("Invalid API key format. API key must start with 'sk-Xnai-'");
                            return AuthenticateResult.Fail("Invalid API key format");
                        }

                        // Look up API key
                        ApiKey? apiKey;
                        
                        if (string.IsNullOrEmpty(tenantId))
                        {
                            // No tenantId provided - look up API key without tenant scoping
                            // This prevents IDOR by deriving tenant from the authenticated credential
                            apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken);
                            if (apiKey == null)
                            {
                                _logger.LogWarning("Invalid API key submitted");
                                return AuthenticateResult.Fail("Invalid API key");
                            }
                        }
                        else
                        {
                            // tenantId provided (legacy support) - validate it matches the API key
                            apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken, tenantId);
                            if (apiKey == null || apiKey.TenantId != tenantId)
                            {
                                _logger.LogWarning("API key does not match provided tenant {TenantId}", tenantId);
                                return AuthenticateResult.Fail("Invalid API key or Tenant ID");
                            }
                        }

                        // Validate that the API key creator is a SysAdmin
                        var isSysAdmin = await _userRepository.IsSysAdmin(apiKey.CreatedBy);
                        if (!isSysAdmin)
                        {
                            _logger.LogWarning("API key creator {UserId} is not a SysAdmin. Access denied.", apiKey.CreatedBy);
                            return AuthenticateResult.Fail("Access denied: Only system administrators can access AdminApi endpoints");
                        }
                        
                        _logger.LogDebug("Setting tenant context with user ID: {userId} and user type: {userType}", apiKey.CreatedBy, UserType.UserApiKey);
                        _tenantContext.LoggedInUser = apiKey.CreatedBy;
                        _tenantContext.UserType = UserType.UserApiKey;
                        _tenantContext.TenantId = tenantId;
                        _tenantContext.AuthorizedTenantIds = new[] { apiKey.TenantId };
                        _tenantContext.Authorization = accessToken;

                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
                            new Claim("TenantId", apiKey.TenantId)
                        };

                        var identity = new ClaimsIdentity(claims, Scheme.Name);
                        var principal = new ClaimsPrincipal(identity);
                        var ticket = new AuthenticationTicket(principal, Scheme.Name);
                        _logger.LogInformation("Successfully authenticated AdminApi connection: User={UserId}, Tenant={TenantId}", apiKey.CreatedBy, apiKey.TenantId);

                        return AuthenticateResult.Success(ticket);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for AdminApi Endpoint connection");
                        return AuthenticateResult.Fail("Error processing access token for AdminApi Endpoint connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for AdminApi Endpoint connection");
                    return AuthenticateResult.Fail("No access token found for AdminApi Endpoint connection");
                }
            }
            else
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                return AuthenticateResult.Fail("Failed to resolve ITenantContext from request scope");
            }
        }

    }
}

