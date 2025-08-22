﻿using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;

namespace Features.UserApi.Auth
{
    public class ValidWebsocketAccessHandler : AuthorizationHandler<ValidWebsocketAccessRequirement>
    {
        private readonly ILogger<ValidWebsocketAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        public ValidWebsocketAccessHandler(
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
                    _logger.LogDebug("Setting tenant context with user ID: {userId}", loggedInUser);
                    _tenantContext.LoggedInUser = loggedInUser;
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
