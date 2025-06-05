using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;

namespace XiansAi.Server.Shared.Auth
{
    public class ValidWebsocketAccessHandler : AuthorizationHandler<ValidWebsocketAccessRequirement>
    {
        private readonly ILogger<ValidWebsocketAccessHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly ITenantContext _tenantContext;
        public ValidWebsocketAccessHandler(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ITenantContext tenantContext,
            ILogger<ValidWebsocketAccessHandler> logger)
        {
            _configuration = configuration;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidWebsocketAccessRequirement requirement)
        {
            var accessToken = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = context.User.FindFirst("TenantId")?.Value;

            if (_tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                context.Fail();
                return Task.CompletedTask;
            }

            // Check if WebSockets are enabled in configuration
            var websocketConfig = _configuration.GetSection("WebSockets");
            if (!websocketConfig.GetValue<bool>("Enabled"))
            {
                _logger.LogWarning("WebSockets are not enabled in configuration");
                context.Fail();
                return Task.CompletedTask;
            }

            // Get tenantId from query string 
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("No tenantId provided in query string");
                context.Fail();
                return Task.CompletedTask;
            }

            // Extract the token from query string (WebSocket) or Authorization header (negotiate)

            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    // Get the secrets section
                    var secrets = websocketConfig.GetSection("Secrets").Get<Dictionary<string, string>>();
                    // Check if the secrets section is null
                    if (secrets != null)
                    {
                        // Check if the provided tenantId exists in configuration
                        if (!secrets.ContainsKey(tenantId))
                        {
                            _logger.LogWarning("Provided tenantId {TenantId} not found in configuration", tenantId);
                            context.Fail();
                            return Task.CompletedTask;
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
                                context.Fail();
                                return Task.CompletedTask;
                            }
                            _tenantContext.LoggedInUser = websocketUserId;
                            _tenantContext.TenantId = tenantId;
                            _tenantContext.AuthorizedTenantIds = new[] { tenantId };


                            _logger.LogInformation("Successfully authenticated SignalR connection");
                            context.Succeed(requirement);

                        }
                        else
                        {
                            _logger.LogWarning("Access token does not match the expected secret for tenant");
                            context.Fail();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("WebSocket Secrets not found in configuration");
                        context.Fail();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing access token for SignalR connection");
                    context.Fail();
                }
            }
            else
            {
                _logger.LogWarning("No access token found for SignalR connection");
            }
            return Task.CompletedTask;
        }
    }
}
