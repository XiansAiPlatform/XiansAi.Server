using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace XiansAi.Server.Shared.Auth
{
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IAuthorizationCacheService _authorizationCacheService;


        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            IAuthorizationCacheService authorizationCacheService,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
            _authorizationCacheService = authorizationCacheService;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Example: validate access_token and tenant from query
            var accessToken = Request.Query["access_token"].ToString();
            var tenantId = Request.Query["tenantId"].ToString();
            var userAccessToken = Request.Query["userAccessToken"].ToString();
            string? accessGuid = null;
            if (!string.IsNullOrEmpty(userAccessToken))
            {
                accessGuid = await _authorizationCacheService.CacheAuthorization(userAccessToken);
            }
            
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

                if (string.IsNullOrEmpty(userAccessToken))
                {
                    return AuthenticateResult.Fail("No User Access Token provided in query string");
                }
                //if (string.IsNullOrEmpty(accessGuid)) {
                //    return Task.FromResult(AuthenticateResult.Fail("No User Access Token provided in query string"));
                //}

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
                        // Get the secrets section
                        var secrets = websocketConfig.GetSection("Secrets").Get<Dictionary<string, string>>();
                        // Check if the secrets section is null
                        if (secrets == null)
                        {
                            _logger.LogWarning("WebSocket Secrets not found in configuration");
                            return AuthenticateResult.Fail("WebSocket Secrets not found in configuration");
                        }
                        // Check if the provided tenantId exists in configuration
                        if (!secrets.ContainsKey(tenantId))
                        {
                            _logger.LogWarning("Provided tenantId {TenantId} not found in configuration", tenantId);
                            return AuthenticateResult.Fail("Provided tenantId not found in configuration");
                        }

                        // Get the expected secret for this tenant
                        var expectedSecret = secrets[tenantId];

                        // Verify if the provided access token matches the expected secret
                        if (accessToken == expectedSecret)
                        {
                            // Set the WebSocket user ID from configuration
                            var websocketUserId = websocketConfig.GetValue<string>("UserId");
                            
                            if (websocketUserId == null)
                            {
                                _logger.LogWarning("WebSocket UserId not found in configuration");
                                return AuthenticateResult.Fail("WebSocket UserId not found in configuration");
                            }
                            _tenantContext.LoggedInUser = websocketUserId;
                            _tenantContext.TenantId = tenantId;
                            _tenantContext.AuthorizedTenantIds = new[] { tenantId };

                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, accessToken),
                                new Claim("TenantId", tenantId)
                            };
                            if (!string.IsNullOrEmpty(accessGuid))
                            {
                                claims.Add(new Claim("UserAccessGuid", accessGuid));
                            }

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogInformation("Successfully authenticated SignalR connection: User={UserId}, Tenant={TenantId}", websocketUserId, tenantId);

                            return AuthenticateResult.Success(ticket);
                        }
                        else
                        {
                            _logger.LogWarning("Access token does not match the expected secret for tenant {TenantId}", tenantId);
                            return AuthenticateResult.Fail("Access token does not match the expected secret for tenant");
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
