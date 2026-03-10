using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using Shared.Exceptions;
using Shared.Services;
using System.Security.Claims;

namespace Features.AdminApi.Auth
{
    public class ValidAdminEndpointAccessHandler : AuthorizationHandler<ValidAdminEndpointAccessRequirement>
    {
        private readonly ILogger<ValidAdminEndpointAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IApiKeyService _apiKeyService;
        private readonly IAdminRoleTenantResolver _adminRoleTenantResolver;

        public ValidAdminEndpointAccessHandler(
            ITenantContext tenantContext,
            IHttpContextAccessor httpContextAccessor,
            IApiKeyService apiKeyService,
            IAdminRoleTenantResolver adminRoleTenantResolver,
            ILogger<ValidAdminEndpointAccessHandler> logger)
        {
            _tenantContext = tenantContext;
            _httpContextAccessor = httpContextAccessor;
            _apiKeyService = apiKeyService;
            _adminRoleTenantResolver = adminRoleTenantResolver;
            _logger = logger;
        }

        /// <summary>
        /// Returns true if TenantContext was already fully populated by AdminEndpointAuthenticationHandler.
        /// When true, we can skip the redundant API key lookup and role resolution.
        /// </summary>
        private static bool IsContextAlreadyPopulatedByAuth(ITenantContext tenantContext)
        {
            if (string.IsNullOrEmpty(tenantContext.LoggedInUser) ||
                string.IsNullOrEmpty(tenantContext.TenantId) ||
                tenantContext.UserRoles == null)
                return false;

            return tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) ||
                   tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin);
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidAdminEndpointAccessRequirement requirement)
        {
            var loggedInUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (_tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                context.Fail();
                return;
            }

            if (string.IsNullOrEmpty(loggedInUser))
            {
                _logger.LogWarning("No Logged In User");
                context.Fail();
                return;
            }

            // Short-circuit: Authentication handler already did full resolution and populated TenantContext.
            // Skip redundant API key lookup and role resolution to avoid duplicate DB/validation work.
            if (IsContextAlreadyPopulatedByAuth(_tenantContext))
            {
                _logger.LogDebug("AdminApi authorization: TenantContext already populated by authentication - skipping redundant resolution");
                context.Succeed(requirement);
                return;
            }

            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                
                // Extract access token from HTTP context if not already set in tenant context.
                // Authorization header only; query parameter is not supported (leaks into logs, browser history).
                var accessToken = _tenantContext.Authorization;
                if (string.IsNullOrEmpty(accessToken) && httpContext != null)
                {
                    var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        accessToken = authHeader.Substring("Bearer ".Length).Trim();
                    }
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("No access token found for authorization validation");
                    context.Fail();
                    return;
                }

                // Get API key to access its TenantId
                var apiKey = await _apiKeyService.GetApiKeyByRawKeyAsync(accessToken);
                if (apiKey == null)
                {
                    _logger.LogWarning("Invalid API key for authorization validation");
                    context.Fail();
                    return;
                }

                // Extract tenantId from request (query, route, or header) for SysAdmin validation
                var tenantIdFromRequest = string.Empty;
                if (httpContext != null)
                {
                    // 1. Query parameter
                    tenantIdFromRequest = httpContext.Request.Query["tenantId"].ToString();
                    if (string.IsNullOrEmpty(tenantIdFromRequest))
                    {
                        // 2. Route parameter
                        if (httpContext.Request.RouteValues.TryGetValue("tenantId", out var routeTenantId) && routeTenantId != null)
                        {
                            tenantIdFromRequest = routeTenantId.ToString() ?? string.Empty;
                        }
                    }
                    if (string.IsNullOrEmpty(tenantIdFromRequest))
                    {
                        // 3. X-Tenant-Id header
                        tenantIdFromRequest = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? string.Empty;
                    }
                }

                var resolutionResult = await _adminRoleTenantResolver.ResolveAsync(
                    loggedInUser, apiKey, tenantIdFromRequest);

                if (!resolutionResult.Success)
                {
                    _logger.LogWarning("Admin role resolution failed: {Error}", resolutionResult.ErrorMessage);
                    context.Fail();
                    return;
                }

                var finalTenantId = resolutionResult.FinalTenantId!;
                var userRoles = resolutionResult.UserRoles!;

                _logger.LogDebug("Setting tenant context with user ID: {userId}, user type: {userType}, and roles: {roles}", 
                    loggedInUser, UserType.UserApiKey, string.Join(", ", userRoles));
                _tenantContext.LoggedInUser = loggedInUser;
                _tenantContext.UserType = UserType.UserApiKey;
                _tenantContext.TenantId = finalTenantId;
                _tenantContext.UserRoles = userRoles.ToArray();
                _tenantContext.AuthorizedTenantIds = new[] { finalTenantId };
                _tenantContext.Authorization = accessToken;

                _logger.LogInformation("Successfully authorized AdminApi Endpoint Connection: User={UserId}, Tenant={TenantId}, Roles={Roles}", 
                    loggedInUser, finalTenantId, string.Join(", ", userRoles));
                context.Succeed(requirement);
            }
            catch (TenantNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorization for AdminApi Endpoint connection");
                context.Fail();
            }
        }
    }
}


