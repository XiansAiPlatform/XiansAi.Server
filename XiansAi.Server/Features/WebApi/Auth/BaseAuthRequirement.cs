using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Features.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using XiansAi.Server.Auth;

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

    protected BaseAuthHandler(
        ILogger logger, 
        ITenantContext tenantContext)
    {
        _logger = logger;
        _tenantContext = tenantContext;
    }

    protected virtual (bool success, string? loggedInUser, IEnumerable<string>? authorizedTenantIds)
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
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return (false, null, null);
            }

            var loggedInUser = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            
            if (string.IsNullOrEmpty(loggedInUser))
            {
                _logger.LogWarning("No user identifier found in token");
                return (false, null, null);
            }

            var authorizedTenantIds = jsonToken.Claims
                .Where(c => c.Type == BaseAuthRequirement.TENANT_CLAIM_TYPE)
                .Select(c => c.Value)
                .ToList();
            
            // Set the user name to the logged in user
            httpContext?.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, loggedInUser)]));

            return (true, loggedInUser, authorizedTenantIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null);
        }
    }
} 