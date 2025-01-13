using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;

namespace XiansAi.Server.Auth;

public class Auth0ClientRequirement : IAuthorizationRequirement
{    
    public Auth0ClientRequirement(IConfiguration configuration)
    {
    }
}

public class Auth0ClientHandler : AuthorizationHandler<Auth0ClientRequirement>
{
    private readonly ILogger<Auth0ClientHandler> _logger;
    private readonly ITenantContext _tenantContext;

    public Auth0ClientHandler(
        ILogger<Auth0ClientHandler> logger, 
        ITenantContext tenantContext)
    {
        _logger = logger;
        _tenantContext = tenantContext;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        Auth0ClientRequirement requirement)
    {
        // Get token from Authorization header instead of claims
        var httpContext = context.Resource as HttpContext;
        var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return Task.CompletedTask;
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                context.Fail();
                return Task.CompletedTask;
            }

            var loggedInUser = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
            
            if (string.IsNullOrEmpty(loggedInUser))
            {
                _logger.LogWarning("No user identifier found in token");
                context.Fail();
                return Task.CompletedTask;
            }

            var authorizedTenantIds = jsonToken.Claims
                .Where(c => c.Type == JwtClientRequirement.TENANT_CLAIM_TYPE)
                .Select(c => c.Value)
                .ToList();


            _logger.LogInformation("Authorized tenant IDs: {authorizedTenantIds}", authorizedTenantIds);
            _logger.LogInformation("Logged in user: {loggedInUser}", loggedInUser);

            // set tenant context and succeed
            _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
            _tenantContext.LoggedInUser = loggedInUser;
            
            _logger.LogInformation("Authorization requirement succeeded");
            context.Succeed(requirement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            context.Fail();
        }

        return Task.CompletedTask;
    }
}