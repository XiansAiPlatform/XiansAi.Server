using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Shared.Utils;

namespace Features.UserApi.Auth
{
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IApiKeyService _apiKeyService;
        private readonly IDynamicOidcValidator _dynamicOidcValidator;


        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService,
            IDynamicOidcValidator dynamicOidcValidator)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
            _apiKeyService = apiKeyService;
            _dynamicOidcValidator = dynamicOidcValidator;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Only handle authentication for specific websocket/SignalR endpoints
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            var isWebsocketEndpoint = path.StartsWith("/ws/");
            
            _logger.LogDebug("WebsocketAuthenticationHandler: Evaluating path '{Path}' - IsWebsocketEndpoint: {IsWebsocketEndpoint}", Request.Path, isWebsocketEndpoint);
            
            if (!isWebsocketEndpoint)
            {
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }

            _logger.LogDebug("Processing SignalR/Websocket request: {Path}", Request.Path);

            // Check for access token in multiple locations: apikey query param, access_token query param, or Authorization header
            var accessToken = Request.Query["apikey"].ToString();
            var tenantId = Request.Query["tenantId"].ToString();

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

                // Check for token in different locations based on authentication method
                if (string.IsNullOrEmpty(accessToken))
                {
                    // Check for access_token query parameter (used for JWT and as fallback for API keys)
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
                            if (Request.Path.HasValue && Request.Path.Value.Contains("/ws/tenant/chat", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Skipping JWT validation for /ws/tenant/chat endpoint");
                                return AuthenticateResult.Fail("/ws/tenant/chat endpoint does not support JWT validation");
                            }
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

                            var tenantIds = new List<string>
                            {
                                tenantId
                            };

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
                            _logger.LogDebug("Successfully authenticated Websocket JWT: User={UserId}, Tenant={TenantId}", userId, tenantId);
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
                        _logger.LogError(ex, "Error processing access token for Websocket connection");
                        return AuthenticateResult.Fail("Error processing access token for Websocket connection");
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for Websocket connection");
                    return AuthenticateResult.Fail("No access token found for Websocket connection");
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
