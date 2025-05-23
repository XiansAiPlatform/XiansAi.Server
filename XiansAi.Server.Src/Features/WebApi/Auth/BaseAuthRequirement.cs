using System.Security.Claims;
using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers;

namespace Features.WebApi.Auth;

public abstract class BaseAuthRequirement : IAuthorizationRequirement
{
    public static string TENANT_CLAIM_TYPE = "https://xians.ai/tenants";
    
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

    protected virtual async Task<(bool success, string? loggedInUser, IEnumerable<string>? authorizedTenantIds)>
        ValidateToken(AuthorizationHandlerContext context)
    {
        var httpContext = context.Resource as HttpContext;
        var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return (false, null, null);
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var authProvider = _authProviderFactory.GetProvider();
            var (success, userId, tenantIds) = await authProvider.ValidateToken(token);
            
            if (!success || string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Token validation failed");
                return (false, null, null);
            }
            
            // Set the user name to the logged in user
            httpContext?.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId)]));

            return (true, userId, tenantIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null);
        }
    }
} 