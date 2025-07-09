using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Services;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Features.UserApi.Auth
{
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly IApiKeyService _apiKeyService;


        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext,
            IApiKeyService apiKeyService)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
            _apiKeyService = apiKeyService;
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
                                // Treat as JWT
                            if (Request.Path.HasValue && Request.Path.Value.Contains("/ws/tenant/chat", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Skipping JWT validation for /ws/tenant/chat endpoint");
                                return AuthenticateResult.Fail("/ws/tenant/chat endpoint does not support JWT validation");
                            }
                            // Treat as JWT
                            // TODO: Need to add jwt validation logic here
                            _logger.LogWarning("JWT authentication not implemented");
                            return AuthenticateResult.Fail("JWT authentication not implemented");
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
