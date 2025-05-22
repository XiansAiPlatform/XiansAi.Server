using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers;

namespace Features.WebApi.Auth;

public class ValidTokenRequirement : BaseAuthRequirement
{    
    public ValidTokenRequirement(IConfiguration configuration) : base(configuration)
    {
    }
}

public class TokenClientHandler : BaseAuthHandler<ValidTokenRequirement>
{
    public TokenClientHandler(
        ILogger<TokenClientHandler> logger, 
        ITenantContext tenantContext,
        IAuthProviderFactory authProviderFactory)
        : base(logger, tenantContext, authProviderFactory)
    {
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidTokenRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = await ValidateToken(context);
        
        if (!success)
        {
            context.Fail();
            return;
        }

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds ?? new List<string>();
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");
        
        _logger.LogInformation("Authorization requirement succeeded");
        context.Succeed(requirement);
    }
}