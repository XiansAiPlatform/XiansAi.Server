using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;

namespace Features.UserApi.Auth
{
    public class ValidWebsocketAccessHandler : AuthorizationHandler<ValidWebsocketAccessRequirement>
    {
        private readonly ILogger<ValidWebsocketAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        public ValidWebsocketAccessHandler(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ITenantContext tenantContext,
            ILogger<ValidWebsocketAccessHandler> logger)
        {
            _tenantContext = tenantContext;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidWebsocketAccessRequirement requirement)
        {
            var loginedUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = context.User.FindFirst("TenantId")?.Value;

            if (_tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                context.Fail();
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("No tenantId provided in query string");
                context.Fail();
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(loginedUser))
            {
                _logger.LogWarning("No LoginedUser");
                context.Fail();
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(loginedUser))
            {
                try
                {
                    _tenantContext.LoggedInUser = loginedUser;
                    _tenantContext.TenantId = tenantId;
                    _tenantContext.AuthorizedTenantIds = new[] { tenantId };

                    _logger.LogInformation("Successfully authenticated Websocket Connection");
                    context.Succeed(requirement);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing access token for Websocket connection");
                    context.Fail();
                }
            }
            else
            {
                _logger.LogWarning("Authorization Fails for Websocket connection");
            }
            return Task.CompletedTask;
        }
    }
}
