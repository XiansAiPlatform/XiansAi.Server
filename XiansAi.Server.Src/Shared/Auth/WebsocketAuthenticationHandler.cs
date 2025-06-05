using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace XiansAi.Server.Shared.Auth
{
    public class WebsocketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<WebsocketAuthenticationHandler> _logger;
        private readonly IConfiguration _configuration;


        public WebsocketAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration,
            ITenantContext tenantContext)
            : base(options, logger, encoder)
        {
            _logger = logger.CreateLogger<WebsocketAuthenticationHandler>();
            _tenantContext = tenantContext;
            _configuration = configuration;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
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
                    return Task.FromResult(AuthenticateResult.Fail("WebSockets are not enabled in configuration"));
                }

                if (string.IsNullOrEmpty(tenantId))
                {
                    return Task.FromResult(AuthenticateResult.Fail("No tenantId provided in query string"));
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
                        // Get the secrets section
                        var secrets = websocketConfig.GetSection("Secrets").Get<Dictionary<string, string>>();
                        // Check if the secrets section is null
                        if (secrets == null)
                        {
                            _logger.LogWarning("WebSocket Secrets not found in configuration");
                            return Task.FromResult(AuthenticateResult.Fail("WebSocket Secrets not found in configuration"));
                        }
                        // Check if the provided tenantId exists in configuration
                        if (!secrets.ContainsKey(tenantId))
                        {
                            _logger.LogWarning("Provided tenantId {TenantId} not found in configuration", tenantId);
                            return Task.FromResult(AuthenticateResult.Fail("Provided tenantId not found in configuration"));
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
                                return Task.FromResult(AuthenticateResult.Fail("WebSocket UserId not found in configuration"));
                            }
                            _tenantContext.LoggedInUser = websocketUserId;
                            _tenantContext.TenantId = tenantId;
                            _tenantContext.AuthorizedTenantIds = new[] { tenantId };

                            var claims = new[]
                            {
                                new Claim(ClaimTypes.NameIdentifier, accessToken),
                                new Claim("TenantId", tenantId),
                            };

                            var identity = new ClaimsIdentity(claims, Scheme.Name);
                            var principal = new ClaimsPrincipal(identity);
                            var ticket = new AuthenticationTicket(principal, Scheme.Name);
                            _logger.LogInformation("Successfully authenticated SignalR connection: User={UserId}, Tenant={TenantId}", websocketUserId, tenantId);

                            return Task.FromResult(AuthenticateResult.Success(ticket));
                        }
                        else
                        {
                            _logger.LogWarning("Access token does not match the expected secret for tenant {TenantId}", tenantId);
                            return Task.FromResult(AuthenticateResult.Fail("Access token does not match the expected secret for tenant"));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing access token for SignalR connection");
                        return Task.FromResult(AuthenticateResult.Fail("Error processing access token for SignalR connection"));
                    }
                }
                else
                {
                    _logger.LogWarning("No access token found for SignalR connection");
                    return Task.FromResult(AuthenticateResult.Fail("No access token found for SignalR connection"));
                }
            }
            else
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                return Task.FromResult(AuthenticateResult.Fail("Failed to resolve ITenantContext from request scope"));
            }
        }
    }
}
