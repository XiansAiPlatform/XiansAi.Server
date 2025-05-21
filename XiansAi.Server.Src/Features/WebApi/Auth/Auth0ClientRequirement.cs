using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;

namespace Features.WebApi.Auth;

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

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        Auth0ClientRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);
        
        if (!success)
        {
            context.Fail();
            return Task.CompletedTask;
        }


        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds ?? new List<string>();
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");
        
        _logger.LogInformation("Authorization requirement succeeded");
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}