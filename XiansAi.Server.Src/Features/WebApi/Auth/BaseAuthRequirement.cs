using System.Security.Claims;
using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers;

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

    protected BaseAuthHandler(
        ILogger logger, 
        ITenantContext tenantContext,
        IAuthProviderFactory authProviderFactory)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _authProviderFactory = authProviderFactory;
    }

    protected virtual async Task<(bool success, string? loggedInUser)>
        ValidateToken(AuthorizationHandlerContext context)
    {
        var httpContext = context.Resource as HttpContext;
        var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return (false, null);
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var authProvider = _authProviderFactory.GetProvider();
            var (success, userId) = await authProvider.ValidateToken(token);
            
            if (!success || string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Token validation failed");
                return (false, null);
            }
            
            // Set the user name to the logged in user
            httpContext?.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)]));

            return (true, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null);
        }
    }
} 