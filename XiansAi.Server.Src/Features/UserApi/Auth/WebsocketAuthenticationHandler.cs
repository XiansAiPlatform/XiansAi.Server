using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Providers.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.IdentityModel.Tokens.Jwt;

namespace Features.UserApi.Auth
{
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IApiKeyService _apiKeyService;
        private readonly ITokenServiceFactory _tokenServiceFactory;
        private readonly IAuthMgtConnect _authMgtConnect;

        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService,
            ITokenServiceFactory tokenServiceFactory,
            IAuthMgtConnect authMgtConnect)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
            _apiKeyService = apiKeyService;
            _tokenServiceFactory = tokenServiceFactory;
            _authMgtConnect = authMgtConnect;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Example: validate access_token and tenant from query
            var accessToken = Request.Query["access_token"].ToString();
            var tenantId = Request.Query["tenantId"].ToString();

            _logger.LogDebug("Processing SignalR request: {Path}", Request.Path);
            if (_tenantContext != null)
            {
                // Check if WebSockets are enabled in configuration
                var websocketConfig = _configuration.GetSection("WebSockets");
                if (!websocketConfig.GetValue<bool>("Enabled"))
                {
                    _logger.LogWarning("WebSockets are not enabled in configuration");
                    return AuthenticateResult.Fail("WebSockets are not enabled in configuration");
                }

                if (string.IsNullOrEmpty(tenantId))
                {
                    return AuthenticateResult.Fail("No tenantId provided in query string");
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    var authHeader = Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                    {
                        accessToken = authHeader.Substring("Bearer ".Length);
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
                                _logger.LogWarning("WebSocket apikey not found");
                                return AuthenticateResult.Fail("Invalid API key or Tenant ID");
                            }

                            if (tenantId == apiKey.TenantId)
                            {
                                _tenantContext.LoggedInUser = apiKey.CreatedBy;
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
                            if (Request.Path.HasValue && Request.Path.Value.Contains("/ws/tenant/chat", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Skipping JWT validation for /ws/tenant/chat endpoint");
                                return AuthenticateResult.Fail("/ws/tenant/chat endpoint does not support JWT validation");
                            }
                            // Treat as JWT
                            var tokenService = _tokenServiceFactory.GetTokenService();
                            var jwtResult = await tokenService.ProcessToken(accessToken);
                            if (string.IsNullOrEmpty(jwtResult.userId))
                            {
                                _logger.LogInformation("No user Id found in the JWT Token");
                                return AuthenticateResult.Fail("No user Id found in the JWT Token");
                            }
                            var tenantIds = await _authMgtConnect.GetUserTenants(jwtResult.userId);
                            foreach (var tId in tenantIds)
                            {
                                _logger.LogDebug("---------tenantIds-{tId}: ", tId);
                            }

                            //var handler = new JwtSecurityTokenHandler();
                            //var jwtToken = handler.ReadJwtToken(accessToken);
                            //var tenantIds =  tokenService.ExtractTenantIds(jwtToken);
                            if (!jwtResult.success || string.IsNullOrEmpty(jwtResult.userId) || string.IsNullOrEmpty(tenantId) || !tenantIds.Contains(tenantId))
                            {
                                _logger.LogWarning("JWT authentication failed or tenant mismatch");
                                return AuthenticateResult.Fail("JWT authentication failed or tenant mismatch");
                            }

                            _tenantContext.LoggedInUser = jwtResult.userId;
                            _tenantContext.TenantId = tenantId;
                            _tenantContext.AuthorizedTenantIds = tenantIds;
                            _logger.LogDebug("UserID-{Id}", jwtResult.userId);
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, jwtResult.userId),
                                new Claim("TenantId", tenantId)
                            };
                            foreach (var tId in tenantIds)
                            {
                                _logger.LogDebug("tenantIds-{tId}",tId);
                                claims.Add(new Claim("AuthorizedTenantId", tId));
                            }

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogDebug("Successfully authenticated Websocket JWT: User={UserId}, Tenant={TenantId}", jwtResult.userId, tenantId);
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
                        _logger.LogError(ex, "Error processing access token for SignalR connection");
                        return AuthenticateResult.Fail("Error processing access token for SignalR connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for SignalR connection");
                    return AuthenticateResult.Fail("No access token found for SignalR connection");
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
