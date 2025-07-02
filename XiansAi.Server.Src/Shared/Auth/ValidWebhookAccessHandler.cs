using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;

namespace XiansAi.Server.Shared.Auth
{
    public class ValidWebhookAccessHandler : AuthorizationHandler<ValidWebhookAccessRequirement>
    {
        private readonly ILogger<ValidWebhookAccessHandler> _logger;
        private readonly IConfiguration _configuration;
        private readonly ITenantContext _tenantContext;
        public ValidWebhookAccessHandler(
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            ITenantContext tenantContext,
            ILogger<ValidWebhookAccessHandler> logger)
        {
            _configuration = configuration;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidWebhookAccessRequirement requirement)
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

                    _logger.LogInformation("Successfully authenticated Webhook Connection");
                    context.Succeed(requirement);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing access token for Webhook connection");
                    context.Fail();
                }
            }
            else
            {
                _logger.LogWarning("Authorization Fails for Webhook connection");
            }
            return Task.CompletedTask;
        }
    }

}

