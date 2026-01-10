using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Providers.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Shared.Utils;
using Shared.Data.Models;

namespace Features.UserApi.Auth
{
    public class EndpointAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<EndpointAuthenticationHandler> _logger;
        private readonly IApiKeyService _apiKeyService;
        private readonly ITokenServiceFactory _tokenServiceFactory;
        private readonly IUserTenantService _userTenantService;
        private readonly IDynamicOidcValidator _dynamicOidcValidator;

        public EndpointAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService,
            ITokenServiceFactory tokenServiceFactory,
            IUserTenantService userTenantService,
            IDynamicOidcValidator dynamicOidcValidator)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<EndpointAuthenticationHandler>();
            _tenantContext = tenantContext;
            _apiKeyService = apiKeyService;
            _tokenServiceFactory = tokenServiceFactory;
            _userTenantService = userTenantService;
            _dynamicOidcValidator = dynamicOidcValidator;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Only handle authentication for UserApi endpoints
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            
            _logger.LogDebug("EndpointAuthenticationHandler: Evaluating path '{Path}'", Request.Path);
            
            if (!path.StartsWith("/api/user/"))
            {
                _logger.LogDebug("Skipping endpoint authentication for non-UserApi path: {Path}", Request.Path);
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }

            _logger.LogDebug("Processing UserApi endpoint request: {Path}", Request.Path);

            // Note: Rate limiting is handled by the rate limiting middleware (which runs before authentication)
            // All UserApi endpoints should use .WithAgentUserApiRateLimit() to prevent enumeration attacks

            // Check for access token in multiple locations: apikey query param, access_token query param, or Authorization header
            var accessToken = Request.Query["apikey"].ToString();
            var tenantId = Request.Query["tenantId"].ToString();
            
            // Note: tenantId is now optional. If not provided, it will be derived from the API key.
            // This prevents IDOR vulnerabilities by ensuring the tenant matches the authenticated credential.

            _logger.LogDebug("Processing Endpoint request: {Path}", Request.Path);
            if (_tenantContext != null)
            {
                // Check for token in different locations based on authentication method
                if (string.IsNullOrEmpty(accessToken))
                {
                    // Check for access_token query parameter (used for JWT fallback when EventSource headers aren't supported)
                    accessToken = Request.Query["access_token"].ToString();
                }
                
                if (string.IsNullOrEmpty(accessToken))
                {
                    // Check for Authorization header (preferred method for JWT)
                    var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    var (tokenExtracted, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
                    
                    if (tokenExtracted && token != null)
                    {
                        accessToken = token;
                        _tenantContext.Authorization = accessToken;
                    }
                }

                if (!string.IsNullOrEmpty(accessToken))
                {
                    try
                    {
                        if (accessToken.StartsWith("sk-Xnai-"))
                        {
                            // Treat as API key
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

                            _tenantContext.LoggedInUser = apiKey.CreatedBy;
                            _tenantContext.UserType = UserType.UserApiKey;
                            _tenantContext.TenantId = apiKey.TenantId;
                            _tenantContext.AuthorizedTenantIds = new[] { apiKey.TenantId };

                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, apiKey.CreatedBy),
                                new Claim("TenantId", apiKey.TenantId)
                            };

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogInformation("Successfully authenticated Web connection: User={UserId}, Tenant={TenantId}", apiKey.CreatedBy, apiKey.TenantId);

                            return AuthenticateResult.Success(ticket);

                        }
                        else if (accessToken.Count(c => c == '.') == 2)
                        {
                            // Treat as JWT - validate using dynamic OIDC per-tenant rules
                            // For JWT, tenantId is required to determine which OIDC configuration to use
                            if (string.IsNullOrEmpty(tenantId))
                            {
                                _logger.LogWarning("JWT authentication requires tenantId parameter");
                                return AuthenticateResult.Fail("tenantId query parameter is required for JWT authentication");
                            }
                            
                            var validation = await _dynamicOidcValidator.ValidateAsync(tenantId, accessToken);
                            if (!validation.success || string.IsNullOrEmpty(validation.canonicalUserId))
                            {
                                _logger.LogWarning("JWT validation failed: {Error}", validation.error);
                                return AuthenticateResult.Fail(validation.error ?? "JWT validation failed");
                            }
                            var userId = validation.canonicalUserId;
                            _tenantContext.LoggedInUser = userId;
                            _tenantContext.UserType = UserType.UserToken;

                            var tenantIds = new List<string>();
                            tenantIds.Add(tenantId);

                            if (string.IsNullOrEmpty(tenantId) || !tenantIds.Contains(tenantId))
                            {
                                _logger.LogWarning("JWT authentication failed, tenant mismatch");
                                return AuthenticateResult.Fail("JWT authentication failed, tenant mismatch");
                            }

                            _tenantContext.TenantId = tenantId;
                            _tenantContext.AuthorizedTenantIds = tenantIds;
                            _logger.LogDebug("UserID-{Id}", userId);
                            
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, userId),
                                new Claim("TenantId", tenantId)
                            };
                            
                            foreach (var tId in tenantIds)
                            {
                                _logger.LogDebug("tenantIds-{tId}: ", tId);
                                claims.Add(new Claim("AuthorizedTenantId", tId));
                            }

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogInformation("Successfully authenticated Endpoint JWT: User={UserId}, Tenant={TenantId}", userId, tenantId);
                            
                            return AuthenticateResult.Success(ticket);
                        }
                        else
                        {
                            _logger.LogWarning("Invalid token format");
                            return AuthenticateResult.Fail("Invalid token format");
                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for Endpoint connection");
                        return AuthenticateResult.Fail("Error processing access token for Endpoint connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for Endpoint connection");
                    return AuthenticateResult.Fail("No access token found for Endpoint connection");
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
