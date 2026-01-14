using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Auth;
using Shared.Repositories;
using System.Security.Claims;

namespace Features.AdminApi.Auth
{
    public class ValidAdminEndpointAccessHandler : AuthorizationHandler<ValidAdminEndpointAccessRequirement>
    {
        private readonly ILogger<ValidAdminEndpointAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        private readonly IUserRepository _userRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        public ValidAdminEndpointAccessHandler(
            ITenantContext tenantContext,
            IUserRepository userRepository,
            IHttpContextAccessor httpContextAccessor,
            ILogger<ValidAdminEndpointAccessHandler> logger)
        {
            _tenantContext = tenantContext;
            _userRepository = userRepository;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidAdminEndpointAccessRequirement requirement)
        {
            var loggedInUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = context.User.FindFirst("TenantId")?.Value;

            if (_tenantContext == null)
            {
                _logger.LogError("Failed to resolve ITenantContext from request scope");
                context.Fail();
                return;
            }

            // tenantId is optional - it can be derived from the API key in the authentication handler
            // If tenantId is not in claims, it should be set from the tenant context by the auth handler
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = _tenantContext.TenantId;
            }

            if (string.IsNullOrEmpty(loggedInUser))
            {
                _logger.LogWarning("No Logged In User");
                context.Fail();
                return;
            }

            if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(loggedInUser))
            {
                try
                {
                    // Get user roles for the tenant context
                    var userRoles = await _userRepository.GetUserRolesAsync(loggedInUser, tenantId);
                    
                    // Extract access token from HTTP context if not already set in tenant context
                    var accessToken = _tenantContext.Authorization;
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        var httpContext = _httpContextAccessor.HttpContext;
                        if (httpContext != null)
                        {
                            // Try Authorization header first
                            var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
                            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            {
                                accessToken = authHeader.Substring("Bearer ".Length).Trim();
                            }
                            
                            // Fallback to query parameter
                            if (string.IsNullOrEmpty(accessToken))
                            {
                                accessToken = httpContext.Request.Query["apikey"].ToString();
                            }
                        }
                    }
                    
                    _logger.LogDebug("Setting tenant context with user ID: {userId}, user type: {userType}, and roles: {roles}", 
                        loggedInUser, UserType.UserApiKey, string.Join(", ", userRoles));
                    _tenantContext.LoggedInUser = loggedInUser;
                    _tenantContext.UserType = UserType.UserApiKey;
                    _tenantContext.TenantId = tenantId;
                    _tenantContext.UserRoles = userRoles.ToArray();
                    _tenantContext.AuthorizedTenantIds = new[] { tenantId };
                    _tenantContext.Authorization = accessToken;

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
        }
    }
}


