using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Providers.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

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

            // Check for access token in multiple locations: apikey query param, access_token query param, or Authorization header
            var accessToken = Request.Query["apikey"].ToString();
            var tenantId = Request.Query["tenantId"].ToString();
            
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("No tenantId query string or invalid");
                return AuthenticateResult.Fail("No tenantId provided in query string");
            } 

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
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        accessToken = authHeader.Substring("Bearer ".Length);
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
                            var apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken, tenantId);
                            if (apiKey == null)
                            {
                                _logger.LogWarning("Submitted apiKey not found for tenant {TenantId}", tenantId);
                                return AuthenticateResult.Fail("Invalid API key or Tenant ID");
                            }

                            if (tenantId == apiKey.TenantId)
                            {
                                _logger.LogDebug("Setting tenant context with user ID: {userId} and user type: {userType}", apiKey.CreatedBy, UserType.UserApiKey);
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
                                _logger.LogInformation("Successfully authenticated Web connection: User={UserId}, Tenant={TenantId}", apiKey.CreatedBy, tenantId);

                                return AuthenticateResult.Success(ticket);
                            }
                            else
                            {
                                _logger.LogWarning("Invalid TenantID {TenantId}", tenantId);
                                return AuthenticateResult.Fail("Access denied Invalid TenantID");
                            }
                        }
                        else if (accessToken.Count(c => c == '.') == 2)
                        {
                            // Treat as JWT - validate using dynamic OIDC per-tenant rules
                            var validation = await _dynamicOidcValidator.ValidateAsync(tenantId, accessToken);
                            if (!validation.success || string.IsNullOrEmpty(validation.canonicalUserId))
                            {
                                _logger.LogWarning("JWT validation failed: {Error}", validation.error);
                                return AuthenticateResult.Fail(validation.error ?? "JWT validation failed");
                            }
                            var userId = validation.canonicalUserId;
                            _logger.LogDebug("Setting tenant context with user ID: {userId} and user type: {userType}", userId, UserType.UserToken);
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
