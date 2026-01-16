using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using System.Security.Claims;

namespace Features.AdminApi.Auth
{
    public class ValidAdminEndpointAccessHandler : AuthorizationHandler<ValidAdminEndpointAccessRequirement>
    {
        private readonly ILogger<ValidAdminEndpointAccessHandler> _logger;
        private readonly ITenantContext _tenantContext;
        private readonly IUserRepository _userRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IApiKeyService _apiKeyService;
        
        public ValidAdminEndpointAccessHandler(
            ITenantContext tenantContext,
            IUserRepository userRepository,
            IHttpContextAccessor httpContextAccessor,
            IApiKeyService apiKeyService,
            ILogger<ValidAdminEndpointAccessHandler> logger)
        {
            _tenantContext = tenantContext;
            _userRepository = userRepository;
            _httpContextAccessor = httpContextAccessor;
            _apiKeyService = apiKeyService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ValidAdminEndpointAccessRequirement requirement)
        {
            var loggedInUser = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantIdFromClaim = context.User.FindFirst("TenantId")?.Value;

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

            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                
                // Extract access token from HTTP context if not already set in tenant context
                var accessToken = _tenantContext.Authorization;
                if (string.IsNullOrEmpty(accessToken) && httpContext != null)
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

                // Get user roles - use tenantId from claim if available, otherwise use API key's tenantId
                var tenantIdForRoleCheck = !string.IsNullOrEmpty(tenantIdFromClaim) ? tenantIdFromClaim : apiKey.TenantId;
                var userRoles = await _userRepository.GetUserRolesAsync(loggedInUser, tenantIdForRoleCheck);
                
                // Check if user has SysAdmin or TenantAdmin role
                var hasSysAdmin = userRoles.Contains(SystemRoles.SysAdmin);
                var hasTenantAdmin = userRoles.Contains(SystemRoles.TenantAdmin);
                
                // Validate role requirements
                if (!hasSysAdmin && !hasTenantAdmin)
                {
                    _logger.LogWarning("User {UserId} does not have SysAdmin or TenantAdmin role. Roles: {Roles}", 
                        loggedInUser, string.Join(", ", userRoles));
                    context.Fail();
                    return;
                }
                
                // Determine final tenantId based on role
                string finalTenantId;
                if (hasSysAdmin)
                {
                    // SysAdmin: use provided tenantId if available, otherwise use API key's tenantId
                    if (!string.IsNullOrEmpty(tenantIdFromRequest))
                    {
                        finalTenantId = tenantIdFromRequest;
                        _logger.LogDebug("SysAdmin user {UserId} using provided tenantId: {TenantId}", 
                            loggedInUser, finalTenantId);
                    }
                    else
                    {
                        finalTenantId = apiKey.TenantId;
                        _logger.LogDebug("SysAdmin user {UserId} using API key tenantId: {TenantId}", 
                            loggedInUser, finalTenantId);
                    }
                }
                else
                {
                    // TenantAdmin only - validate tenantId if provided
                    if (!string.IsNullOrEmpty(tenantIdFromRequest))
                    {
                        // If tenantId was provided, it must match the API key's tenantId
                        if (tenantIdFromRequest != apiKey.TenantId)
                        {
                            _logger.LogWarning("TenantAdmin user {UserId} provided tenantId {ProvidedTenantId} that does not match API key tenantId {ApiKeyTenantId}", 
                                loggedInUser, tenantIdFromRequest, apiKey.TenantId);
                            context.Fail();
                            return;
                        }
                        finalTenantId = tenantIdFromRequest;
                        _logger.LogDebug("TenantAdmin user {UserId} validated tenantId: {TenantId}", 
                            loggedInUser, finalTenantId);
                    }
                    else
                    {
                        // No tenantId provided - use API key's tenantId
                        finalTenantId = apiKey.TenantId;
                        _logger.LogDebug("TenantAdmin user {UserId} using API key tenantId: {TenantId}", 
                            loggedInUser, finalTenantId);
                    }
                }
                
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing authorization for AdminApi Endpoint connection");
                context.Fail();
            }
        }
    }
}


