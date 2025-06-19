using Features.WebApi.Auth.Providers;
using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using System.Security.Claims;
using XiansAi.Server.Features.WebApi.Services;

namespace Features.WebApi.Auth;

public abstract class BaseAuthRequirement : IAuthorizationRequirement
{
    public BaseAuthRequirement(IConfiguration configuration)
    {
    }
}

public abstract class BaseAuthHandler<T> : AuthorizationHandler<T> where T : BaseAuthRequirement
{
    protected readonly ILogger _logger;
    protected readonly ITenantContext _tenantContext;
    protected readonly IAuthProviderFactory _authProviderFactory;
    private readonly IRoleCacheService _roleCacheService;

    protected BaseAuthHandler(
        ILogger logger, 
        ITenantContext tenantContext,
        IRoleCacheService roleCacheService,
        IAuthProviderFactory authProviderFactory)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _authProviderFactory = authProviderFactory;
        _roleCacheService = roleCacheService;
    }

    protected virtual async Task<(bool success, string? loggedInUser, IEnumerable<string>? authorizedTenantIds, IEnumerable<string>? roles)>
        ValidateToken(AuthorizationHandlerContext context)
    {
        var httpContext = context.Resource as HttpContext;
        var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return (false, null, null, null);
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var authProvider = _authProviderFactory.GetProvider();
            var (success, userId, tenantIds) = await authProvider.ValidateToken(token);

            if (!success || string.IsNullOrEmpty(userId) || tenantIds == null || !tenantIds.Any())
            {
                _logger.LogWarning("Token validation failed or tenantIds is null/empty");
                return (false, null, null, null);
            }

            // Set the user name to the logged in user
            httpContext?.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)]));

            // Get roles from cache/database
            if (currentTenantId == null)
            {
                _logger.LogWarning("No tenantId found in request header");
                return (false, null, null, null);
            }

            var roles = await _roleCacheService.GetUserRolesAsync(userId, currentTenantId);

            return (true, userId, tenantIds, roles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null, null);
        }
    }
} 