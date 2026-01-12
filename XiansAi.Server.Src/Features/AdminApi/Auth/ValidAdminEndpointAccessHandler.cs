using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;

namespace Features.AdminApi.Auth
{
    public class ValidAdminEndpointAccessHandler : AuthorizationHandler<ValidAdminEndpointAccessRequirement>
    {
        private readonly ILogger<ValidAdminEndpointAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        public ValidAdminEndpointAccessHandler(
            ITenantContext tenantContext,
            ILogger<ValidAdminEndpointAccessHandler> logger)
        {
            _tenantContext = tenantContext;
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidAdminEndpointAccessRequirement requirement)
        {
            var loggedInUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
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

            if (string.IsNullOrEmpty(loggedInUser))
            {
                _logger.LogWarning("No Logged In User");
                context.Fail();
                return Task.CompletedTask;
            }

            if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(loggedInUser))
            {
                try
                {
                    _logger.LogDebug("Setting tenant context with user ID: {userId} and user type: {userType}", loggedInUser, UserType.UserToken);
                    _tenantContext.LoggedInUser = loggedInUser;
                    _tenantContext.UserType = UserType.UserToken;
                    _tenantContext.TenantId = tenantId;
                    _tenantContext.AuthorizedTenantIds = new[] { tenantId };

                    _logger.LogInformation("Successfully authenticated AdminApi Endpoint Connection");
                    context.Succeed(requirement);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing access token for AdminApi Endpoint connection");
                    context.Fail();
                }
            }
            else
            {
                _logger.LogWarning("Authorization Fails for AdminApi Endpoint connection");
            }
            return Task.CompletedTask;
        }
    }
}

