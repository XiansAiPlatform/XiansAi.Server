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
            // Check if path matches /api/{version}/admin pattern (supports v1, v2, etc.)
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            var adminApiPattern = "/api/";
            var adminSuffix = "/admin";
            
            _logger.LogDebug("AdminEndpointAuthenticationHandler: Evaluating path '{Path}'", Request.Path);
            
            // Check if path matches AdminApi pattern: /api/{version}/admin
            if (!path.StartsWith(adminApiPattern) || !path.Contains(adminSuffix))
            {
                _logger.LogDebug("Skipping admin endpoint authentication for non-AdminApi path: {Path}", Request.Path);
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }
            
            // Verify it's actually an AdminApi path (not just any /api/... path)
            var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length < 3 || pathParts[0] != "api" || pathParts[2] != "admin")
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
            
            // Preserve the original tenantId from request for validation
            var originalTenantIdFromRequest = tenantId;
            
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
                            // Set tenantId from the API key
                            tenantId = apiKey.TenantId;
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

                        // Get user roles for the tenant context
                        var userRoles = await _userRepository.GetUserRolesAsync(apiKey.CreatedBy, apiKey.TenantId);
                        
                        // Check if user has SysAdmin or TenantAdmin role
                        var hasSysAdmin = userRoles.Contains(SystemRoles.SysAdmin);
                        var hasTenantAdmin = userRoles.Contains(SystemRoles.TenantAdmin);
                        
                        // Validate role requirements
                        if (!hasSysAdmin && !hasTenantAdmin)
                        {
                            _logger.LogWarning("User {UserId} does not have SysAdmin or TenantAdmin role. Roles: {Roles}", 
                                apiKey.CreatedBy, string.Join(", ", userRoles));
                            return AuthenticateResult.Fail("User does not have required admin role");
                        }
                        
                        // Determine tenantId based on role
                        string finalTenantId;
                        if (hasSysAdmin)
                        {
                            // SysAdmin: use provided tenantId if available, otherwise use API key's tenantId
                            if (!string.IsNullOrEmpty(originalTenantIdFromRequest))
                            {
                                finalTenantId = originalTenantIdFromRequest;
                                _logger.LogDebug("SysAdmin user {UserId} using provided tenantId: {TenantId}", 
                                    apiKey.CreatedBy, finalTenantId);
                            }
                            else
                            {
                                finalTenantId = apiKey.TenantId;
                                _logger.LogDebug("SysAdmin user {UserId} using API key tenantId: {TenantId}", 
                                    apiKey.CreatedBy, finalTenantId);
                            }
                        }
                        else
                        {
                            // TenantAdmin only - validate tenantId if provided
                            if (!string.IsNullOrEmpty(originalTenantIdFromRequest))
                            {
                                // If tenantId was provided, it must match the API key's tenantId
                                if (originalTenantIdFromRequest != apiKey.TenantId)
                                {
                                    _logger.LogWarning("TenantAdmin user {UserId} provided tenantId {ProvidedTenantId} that does not match API key tenantId {ApiKeyTenantId}", 
                                        apiKey.CreatedBy, originalTenantIdFromRequest, apiKey.TenantId);
                                    return AuthenticateResult.Fail("Tenant ID does not match API key tenant");
                                }
                                finalTenantId = originalTenantIdFromRequest;
                                _logger.LogDebug("TenantAdmin user {UserId} validated tenantId: {TenantId}", 
                                    apiKey.CreatedBy, finalTenantId);
                            }
                            else
                            {
                                // No tenantId provided - use API key's tenantId
                                finalTenantId = apiKey.TenantId;
                                _logger.LogDebug("TenantAdmin user {UserId} using API key tenantId: {TenantId}", 
                                    apiKey.CreatedBy, finalTenantId);
                            }
                        }
                        
                        _logger.LogDebug("Setting tenant context with user ID: {userId}, user type: {userType}, and roles: {roles}", 
                            apiKey.CreatedBy, UserType.UserApiKey, string.Join(", ", userRoles));
                        _tenantContext.LoggedInUser = apiKey.CreatedBy;
                        _tenantContext.UserType = UserType.UserApiKey;
                        _tenantContext.TenantId = finalTenantId;
                        _tenantContext.UserRoles = userRoles.ToArray();
                        _tenantContext.AuthorizedTenantIds = new[] { apiKey.TenantId };
                        _tenantContext.Authorization = accessToken;

                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
                            new Claim("TenantId", finalTenantId)
                        };

                        var identity = new ClaimsIdentity(claims, Scheme.Name);
                        var principal = new ClaimsPrincipal(identity);
                        var ticket = new AuthenticationTicket(principal, Scheme.Name);
                        _logger.LogInformation("Successfully authenticated AdminApi connection: User={UserId}, Tenant={TenantId}, Roles={Roles}", 
                            apiKey.CreatedBy, finalTenantId, string.Join(", ", userRoles));

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

