using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;

namespace XiansAi.Server.Auth;

public class Auth0ClientRequirement : BaseAuthRequirement
{    
    public Auth0ClientRequirement(IConfiguration configuration) : base(configuration)
    {
    }
}

public class Auth0ClientHandler : BaseAuthHandler<Auth0ClientRequirement>
{
    public Auth0ClientHandler(
        ILogger<Auth0ClientHandler> logger, 
        ITenantContext tenantContext)
        : base(logger, tenantContext)
    {
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        Auth0ClientRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = ValidateTokenAsync(context);
        
        if (!success)
        {
            context.Fail();
            return;
        }

        _logger.LogInformation("Authorized tenant IDs: {authorizedTenantIds}", authorizedTenantIds);
        _logger.LogInformation("Logged in user: {loggedInUser}", loggedInUser);

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
        _tenantContext.LoggedInUser = loggedInUser;
        
        _logger.LogInformation("Authorization requirement succeeded");
        context.Succeed(requirement);
    }
}